using Catalog.V1;
using Catalog.V2;
using DbUp.Engine;
using Inquiry.DependencyInjection;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static Catalog.Deployment.Migrations;

namespace Catalog.Deployment;

// Runs each step of the migrations guide against one SQL Server instance and writes a line after
// each step's checks pass. It creates and drops its own databases and login.
public static class SchemaEvolution
{
    private const string V1Type = "Inquiry_Tvp_97901ad0afa7a9860614b4af2b675765f70e0cc178bf52ef01c6c39cb91fc5e1";
    private const string V2Type = "Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f";

    public static async Task RunAsync(string serverConnectionString, TextWriter output)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var newDatabase = "evolution_new_" + suffix;
        var existingDatabase = "evolution_existing_" + suffix;
        var login = "evolution_app_" + suffix;
        try
        {
            await ExecuteAsync(serverConnectionString, $"CREATE DATABASE [{newDatabase}]; CREATE DATABASE [{existingDatabase}];");
            await NewDatabaseAsync(Database(serverConnectionString, newDatabase), output);
            await ExistingDatabaseAsync(serverConnectionString, Database(serverConnectionString, existingDatabase), login, output);
            await output.WriteLineAsync("SCHEMA-EVOLUTION-OK");
        }
        finally
        {
            await ExecuteAsync(serverConnectionString, $"""
                IF DB_ID(N'{newDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{newDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{newDatabase}]; END;
                IF DB_ID(N'{existingDatabase}') IS NOT NULL BEGIN ALTER DATABASE [{existingDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{existingDatabase}]; END;
                IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}];
                """);
        }
    }

    private static async Task NewDatabaseAsync(string db, TextWriter output)
    {
        // Release 2 deploys to an empty database: DbUp runs both scripts in order and journals them.
        var applied = Applied(Upgrader(db, Initial, WidenCode).PerformUpgrade());
        await output.WriteLineAsync($"new database: applied {applied}");
        await output.WriteLineAsync($"new database: {await ValidationAsync(db)}, table types {await TableTypeCountAsync(db)}");

        await using var v2 = StartV2(db);
        await V2InsertAsync(v2, "new-1", "new-2");
        await output.WriteLineAsync($"new database: v2 ByCodes returned {(await V2FindAsync(v2, "new-1", "new-2")).Count} rows");

        var rerun = Upgrader(db, Initial, WidenCode);
        await output.WriteLineAsync($"new database: rerun pending {rerun.GetScriptsToExecute().Count}, upgrade required {rerun.IsUpgradeRequired()}");
    }

    private static async Task ExistingDatabaseAsync(string server, string db, string login, TextWriter output)
    {
        // A database that V1 created before the team adopted DbUp, with data in it.
        await ExecuteAsync(db, V1.CatalogSchema.Ddl);
        await using (var v1 = StartV1(db))
        {
            await V1InsertAsync(v1, "legacy-1");
        }

        await output.WriteLineAsync($"existing database: journal {await JournalAsync(db)}, {await ValidationAsync(db)}");

        // Baseline with an engine that knows only 0001, so it cannot mark a later script by mistake.
        var marked = Applied(Upgrader(db, Initial).MarkAsExecuted());
        await output.WriteLineAsync($"existing database: marked {marked} without running it, journal {await JournalAsync(db)}, rows {await RowCountAsync(db)}");
        var pending = Upgrader(db, Initial, WidenCode).GetScriptsToExecute();
        await output.WriteLineAsync($"existing database: pending {string.Join(", ", pending.Select(static s => s.Name))}");

        // A release whose 0002 fails after its ALTER: the per-script transaction rolls back the whole script.
        var failing = new SqlScript(WidenCode.Name, WidenCode.Contents + "\n;THROW 50000, N'Simulated deployment failure.', 1;");
        var failed = Upgrader(db, Initial, failing).PerformUpgrade();
        Expect(!failed.Successful && failed.ErrorScript?.Name == WidenCode.Name, "the faulted 0002 to fail");
        await output.WriteLineAsync($"failure recovery: {failed.ErrorScript!.Name} rolled back, journal {await JournalAsync(db)}, Code {await CodeTypeAsync(db)}, {await ValidationAsync(db)}");

        // Fixing the script and running the release again resumes at 0002.
        var applied = Applied(Upgrader(db, Initial, WidenCode).PerformUpgrade());
        await output.WriteLineAsync($"existing database: applied {applied}, Code {await CodeTypeAsync(db)}");

        // V1 and V2 instances run side by side. Each binds its own table type.
        await using var v2 = StartV2(db);
        await using (var v1 = StartV1(db))
        {
            await output.WriteLineAsync($"coexistence: {await ValidationAsync(db)}, table types {await TableTypeCountAsync(db)}");
            await V1InsertAsync(v1, "old-app");
            await V2InsertAsync(v2, "new-app");
            Expect((await V2FindAsync(v2, "old-app")).Count == 1 && (await V1FindAsync(v1, "new-app")).Count == 1, "each version to read the other's row");
            await output.WriteLineAsync("coexistence: v2 read the v1 row, v1 read the v2 row");
        }

        await ApplicationPrincipalAsync(server, db, login, output);

        // Every V1 instance has stopped, so V2 may now store values that V1 could not.
        var wide = new string('w', 48);
        await V2InsertAsync(v2, wide);
        Expect((await V2FindAsync(v2, wide)).Single().Code == wide, "the 48-character code to round-trip");
        await output.WriteLineAsync("v1 drained: v2 stored and read a 48-character code");

        // Release 3 retires V1's type.
        applied = Applied(Upgrader(db, Initial, WidenCode, RetireV1Type).PerformUpgrade());
        await output.WriteLineAsync($"retire: applied {applied}");
        await output.WriteLineAsync($"retire: {await ValidationAsync(db)}");
        await using (var staleV1 = StartV1(db))
        {
            var error = await CaptureSqlErrorAsync(() => V1FindAsync(staleV1, "old-app"));
            Expect(error?.Message.Contains(V1Type, StringComparison.Ordinal) == true, "V1 to fail on its retired type");
            await output.WriteLineAsync("retire: v1 ByCodes failed with a SqlException that names its type");
        }

        await output.WriteLineAsync($"retire: v2 ByCodes returned {(await V2FindAsync(v2, "legacy-1", "old-app", "new-app", wide)).Count} rows");
        await output.WriteLineAsync($"retire: rerun pending {Upgrader(db, Initial, WidenCode, RetireV1Type).GetScriptsToExecute().Count}");
    }

    // The application login gets table permissions plus EXECUTE and REFERENCES on the type it binds.
    private static async Task ApplicationPrincipalAsync(string server, string db, string login, TextWriter output)
    {
        var secret = "Aa1!" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(server, $"CREATE LOGIN [{login}] WITH PASSWORD = N'{secret}', CHECK_POLICY = OFF;");
        await ExecuteAsync(db, $"CREATE USER [{login}] FOR LOGIN [{login}]; GRANT SELECT, INSERT ON [dbo].[CatalogItems] TO [{login}];");
        var app = new SqlConnectionStringBuilder(db) { UserID = login, Password = secret, Pooling = false }.ConnectionString;

        await using (var v2 = StartV2(app))
        {
            var error = await CaptureSqlErrorAsync(() => V2FindAsync(v2, "new-app"));
            Expect(error is not null, "the call to fail without permissions on the type");
            await output.WriteLineAsync($"application principal: without type permissions, SQL Server error {error!.Number}");
        }

        await ExecuteAsync(db, $"GRANT EXECUTE, REFERENCES ON TYPE::[dbo].[{V2Type}] TO [{login}];");
        await using (var v2 = StartV2(app))
        {
            var rows = await V2FindAsync(v2, "legacy-1", "old-app", "new-app");
            await output.WriteLineAsync($"application principal: with EXECUTE and REFERENCES on the type, v2 ByCodes returned {rows.Count} rows");
        }
    }

    private static ServiceProvider StartV1(string connectionString) =>
        new ServiceCollection().AddInquiry().AddInquirySqlServer(connectionString).AddCatalogV1().BuildServiceProvider();

    private static ServiceProvider StartV2(string connectionString) =>
        new ServiceCollection().AddInquiry().AddInquirySqlServer(connectionString).AddCatalogV2().BuildServiceProvider();

    private static async Task V1InsertAsync(ServiceProvider app, params string[] codes)
    {
        await using var scope = app.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<V1.CatalogItemStore>();
        foreach (var code in codes)
        {
            await store.InsertAsync(new V1.CatalogItem { Code = code });
        }
    }

    private static async Task V2InsertAsync(ServiceProvider app, params string[] codes)
    {
        await using var scope = app.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<V2.CatalogItemStore>();
        foreach (var code in codes)
        {
            await store.InsertAsync(new V2.CatalogItem { Code = code });
        }
    }

    private static async Task<IReadOnlyList<V1.CatalogItem>> V1FindAsync(ServiceProvider app, params string[] codes)
    {
        await using var scope = app.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<V1.CatalogItemStore>().ByCodesAsync(codes);
    }

    private static async Task<IReadOnlyList<V2.CatalogItem>> V2FindAsync(ServiceProvider app, params string[] codes)
    {
        await using var scope = app.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<V2.CatalogItemStore>().ByCodesAsync(codes);
    }

    // Runs each version's ProviderArtifactsValidationSql. No rows means every artifact it needs exists.
    private static async Task<string> ValidationAsync(string db)
    {
        return $"v1 validation {await StatusAsync(V1.CatalogSchema.ProviderArtifactsValidationSql)}, v2 validation {await StatusAsync(V2.CatalogSchema.ProviderArtifactsValidationSql)}";

        async Task<string> StatusAsync(string validationSql)
        {
            await using var connection = new SqlConnection(db);
            await connection.OpenAsync();
            await using var command = new SqlCommand(validationSql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            var statuses = new List<string>();
            while (await reader.ReadAsync())
            {
                statuses.Add(reader.GetString(reader.GetOrdinal("Status")));
            }

            return statuses.Count == 0 ? "ok" : string.Join(", ", statuses);
        }
    }

    private static string Applied(DatabaseUpgradeResult result)
    {
        Expect(result.Successful, "DbUp to succeed: " + result.Error?.Message);
        return string.Join(", ", result.Scripts.Select(static script => script.Name));
    }

    private static async Task<string> JournalAsync(string db) =>
        await ScalarAsync(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaVersions') IS NULL THEN N'absent' ELSE N'present' END") == "absent"
            ? "absent"
            : await ScalarAsync(db, "SELECT STRING_AGG([ScriptName], N', ') WITHIN GROUP (ORDER BY [Id]) FROM [dbo].[SchemaVersions]");

    private static Task<string> CodeTypeAsync(string db) =>
        ScalarAsync(db, "SELECT CONCAT(TYPE_NAME(user_type_id), N'(', max_length / 2, N')') FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.CatalogItems') AND name = N'Code'");

    private static Task<string> TableTypeCountAsync(string db) =>
        ScalarAsync(db, "SELECT CONVERT(nvarchar(10), COUNT(*)) FROM sys.table_types WHERE name LIKE N'Inquiry[_]Tvp[_]%'");

    private static Task<string> RowCountAsync(string db) =>
        ScalarAsync(db, "SELECT CONVERT(nvarchar(10), COUNT(*)) FROM [dbo].[CatalogItems]");

    private static async Task<SqlException?> CaptureSqlErrorAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (SqlException ex)
        {
            return ex;
        }
    }

    private static void Expect(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected " + what + ".");
        }
    }

    private static string Database(string server, string name) =>
        new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
