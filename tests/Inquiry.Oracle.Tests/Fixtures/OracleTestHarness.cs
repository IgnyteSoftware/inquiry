using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Oracle.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>
/// Creates a throwaway Oracle schema (user), runs the Northwind DDL into it, and exposes a configured
/// <see cref="ServiceProvider"/>. The schema is dropped on disposal so parallel test classes never
/// collide on table state.
/// </summary>
/// <remarks>
/// KNOWN LIMITATION — the live CRUD facts in this project are scaffolding and do NOT yet run green
/// against a real server, even with <see cref="ConnectionStringEnvironmentVariable"/> set. The shared
/// <c>Inquiry.Northwind</c> stores bake their SQL against the SQLite dialect (double-quoted
/// identifiers, <c>@</c> parameters). Oracle uses unquoted/uppercase identifiers and <c>:</c> bind
/// variables, so the SQLite-dialect SQL does not resolve against an Oracle schema. The proper fix is an
/// Oracle-analyzer build of the Northwind entities/stores for this test project — tracked as a
/// follow-up, mirroring the same gap documented in the MySQL test harness. The provider's emitted SQL
/// is verified correct by the generator emission tests; this gap is purely a shared-test-fixture
/// dialect mismatch.
///
/// OPEN QUESTION (BindByName) — these integration tests are also where the <c>@</c>-bound-name vs
/// <c>:</c>-SQL prefix-match question is meant to resolve empirically (does
/// <c>OracleCommand.BindByName = true</c> match a parameter added as <c>@Name</c> against a
/// <c>:Name</c> reference in the SQL text?). With no live Oracle available during E2, this remains
/// unverified; the provider ships <c>BindByName = true</c> per the spec's recommended option.
/// </remarks>
internal sealed class OracleTestHarness : IAsyncDisposable
{
    /// <summary>
    /// Connection string to an Oracle database the test process can use to create a throwaway schema.
    /// When unset, <see cref="OracleFactAttribute"/> skips the test rather than failing it.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "INQUIRY_ORACLE_CONNECTION_STRING";

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

    public static async Task<OracleTestHarness> CreateAsync(string? namePrefix = null)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionStringEnvironmentVariable} is not set; OracleFactAttribute should have skipped this test.");

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
            foreach (var statement in SplitStatements(NorthwindSchema.OracleDdl))
            {
                await using var cmd = db.CreateCommand();
                cmd.CommandText = statement;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var services = new ServiceCollection()
            .AddInquiry()
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
