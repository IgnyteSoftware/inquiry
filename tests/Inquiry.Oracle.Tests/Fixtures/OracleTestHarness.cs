using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>
/// Creates a throwaway Oracle schema (user), runs the supplied DDL into it, and exposes a configured
/// <see cref="ServiceProvider"/>. The schema is dropped on disposal so parallel test classes never
/// collide on table state. The admin connection string (the gvenzl image's SYSTEM user) is supplied by
/// <see cref="OracleContainerFixture"/>.
/// </summary>
internal sealed class OracleTestHarness : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _schemaUser;
    private readonly string _schemaPassword;

    private OracleTestHarness(string adminConnectionString, string schemaUser, string schemaPassword, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _schemaUser = schemaUser;
        _schemaPassword = schemaPassword;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static Task<OracleTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.OracleDdl, namePrefix);

    public static async Task<OracleTestHarness> CreateFromDdlAsync(string adminConnectionString, string ddl, string? namePrefix = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToUpperInvariant();
        var schemaUser = prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();
        var schemaPassword = "Pw_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        await using (var admin = new OracleConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using (var create = admin.CreateCommand())
            {
                create.CommandText = $"CREATE USER {schemaUser} IDENTIFIED BY \"{schemaPassword}\"";
                await create.ExecuteNonQueryAsync();
            }

            await using (var grant = admin.CreateCommand())
            {
                grant.CommandText = $"GRANT CONNECT, RESOURCE, UNLIMITED TABLESPACE TO {schemaUser}";
                await grant.ExecuteNonQueryAsync();
            }
        }

        var builder = new OracleConnectionStringBuilder(adminConnectionString)
        {
            UserID = schemaUser,
            Password = schemaPassword,
        };
        var connectionString = builder.ToString();

        await using (var db = new OracleConnection(connectionString))
        {
            await db.OpenAsync();
            // Oracle has no multi-statement batch; execute each CREATE separately.
            foreach (var statement in SplitStatements(ddl))
            {
                await using var cmd = db.CreateCommand();
                cmd.CommandText = statement;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var services = new ServiceCollection()
            .AddInquiry(typeof(CustomerStore).Assembly, typeof(VersionedItemStore).Assembly)
            .AddInquiryOracle(connectionString)
            .BuildServiceProvider();

        return new OracleTestHarness(adminConnectionString, schemaUser, schemaPassword, connectionString, services);
    }

    private static IEnumerable<string> SplitStatements(string ddl)
    {
        foreach (var raw in ddl.Split(';'))
        {
            var statement = raw.Trim();
            if (statement.Length > 0)
            {
                yield return statement;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        // Force-close pooled connections so DROP USER doesn't fail with "user currently connected".
        OracleConnection.ClearAllPools();

        try
        {
            await using var admin = new OracleConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP USER {_schemaUser} CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup. Don't fail the test on teardown.
        }
    }
}
