using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.PostgreSql.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Inquiry.Sample;

/// <summary>
/// Wires the sample to one of the three Inquiry providers based on the
/// <c>Inquiry:Provider</c> configuration value. Ensures the target database exists
/// (creating it on SQL Server / PostgreSQL when missing) and runs the matching
/// Northwind DDL before registering the provider's DI services.
/// </summary>
internal static class InquiryProviderSetup
{
    public static async Task ConfigureAsync(WebApplicationBuilder builder)
    {
        var providerName = builder.Configuration["Inquiry:Provider"] ?? "Sqlite";
        var provider = Parse(providerName);
        var connectionString = builder.Configuration.GetConnectionString(ConnectionStringKey(provider))
            ?? throw new InvalidOperationException(
                $"Missing connection string '{ConnectionStringKey(provider)}' for Inquiry:Provider '{providerName}'. Add it to ConnectionStrings in appsettings.json.");

        // Local-dev convenience and credential hygiene: a single INQUIRY_SAMPLE_DB environment
        // variable overrides the active provider's connection string, so the committed
        // appsettings.json can carry harmless local defaults while real credentials stay out of
        // source control (and out of secret scanners). See README.md.
        var connectionStringOverride = Environment.GetEnvironmentVariable("INQUIRY_SAMPLE_DB");
        if (!string.IsNullOrWhiteSpace(connectionStringOverride))
        {
            connectionString = connectionStringOverride;
        }

        switch (provider)
        {
            case Provider.Sqlite:
                await EnsureSqliteAsync(connectionString).ConfigureAwait(false);
                builder.Services.AddInquiry().AddInquiryGeneratedStores().AddInquirySqlite(connectionString);
                break;
            case Provider.SqlServer:
                await EnsureSqlServerAsync(connectionString).ConfigureAwait(false);
                builder.Services.AddInquiry().AddInquiryGeneratedStores().AddInquirySqlServer(connectionString);
                break;
            case Provider.PostgreSql:
                await EnsurePostgreSqlAsync(connectionString).ConfigureAwait(false);
                builder.Services.AddInquiry().AddInquiryGeneratedStores().AddInquiryPostgreSql(connectionString);
                break;
        }
    }

    private enum Provider { Sqlite, SqlServer, PostgreSql }

    private static Provider Parse(string name) => name.ToLowerInvariant() switch
    {
        "sqlite"     => Provider.Sqlite,
        "sqlserver"  => Provider.SqlServer,
        "postgresql" or "postgres" => Provider.PostgreSql,
        _ => throw new InvalidOperationException(
            $"Unsupported Inquiry:Provider '{name}'. Use 'Sqlite', 'SqlServer', or 'PostgreSql'."),
    };

    private static string ConnectionStringKey(Provider provider) => provider switch
    {
        Provider.Sqlite     => "InquirySample.Sqlite",
        Provider.SqlServer  => "InquirySample.SqlServer",
        Provider.PostgreSql => "InquirySample.PostgreSql",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        // SQLite auto-creates the file on first open; we just need to run the DDL.
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = NorthwindSchema.SqliteDdl;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        var targetBuilder = new SqlConnectionStringBuilder(connectionString);
        var targetDatabase = targetBuilder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new InvalidOperationException(
                "SQL Server connection string is missing the database name (Initial Catalog / Database).");
        }

        // Bootstrap: connect to master with the same credentials and create the target DB if absent.
        var adminBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using (var admin = new SqlConnection(adminBuilder.ToString()))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{targetDatabase.Replace("'", "''")}') IS NULL CREATE DATABASE [{targetDatabase.Replace("]", "]]")}];";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var db = new SqlConnection(connectionString))
        {
            await db.OpenAsync().ConfigureAwait(false);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = NorthwindSchema.SqlServerDdl;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task EnsurePostgreSqlAsync(string connectionString)
    {
        var targetBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = targetBuilder.Database;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is missing the database name (Database).");
        }

        // Bootstrap: connect to the maintenance "postgres" DB and create the target if absent.
        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using (var admin = new NpgsqlConnection(adminBuilder.ToString()))
        {
            await admin.OpenAsync().ConfigureAwait(false);
            await using var existsCmd = admin.CreateCommand();
            existsCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            var parameter = existsCmd.CreateParameter();
            parameter.ParameterName = "name";
            parameter.Value = targetDatabase;
            existsCmd.Parameters.Add(parameter);
            var exists = await existsCmd.ExecuteScalarAsync().ConfigureAwait(false) is not null;

            if (!exists)
            {
                await using var createCmd = admin.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE \"{targetDatabase.Replace("\"", "\"\"")}\";";
                await createCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await using (var db = new NpgsqlConnection(connectionString))
        {
            await db.OpenAsync().ConfigureAwait(false);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = NorthwindSchema.PostgreSqlDdl;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
