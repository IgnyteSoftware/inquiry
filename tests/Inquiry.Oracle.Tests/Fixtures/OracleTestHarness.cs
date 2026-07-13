using Inquiry.DependencyInjection;
using Inquiry.FeatureCatalog;
using Inquiry.IntegrationTesting;
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
    private int _disposeState;

    private OracleTestHarness(string adminConnectionString, string schemaUser, string connectionString, ServiceProvider services)
    {
        _adminConnectionString = adminConnectionString;
        _schemaUser = schemaUser;
        ConnectionString = connectionString;
        Services = services;
    }

    public string ConnectionString { get; }

    internal string SchemaUser => _schemaUser;

    internal Exception? CleanupFailure { get; private set; }

    public ServiceProvider Services { get; }

    public T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    public static Task<OracleTestHarness> CreateAsync(
        string adminConnectionString,
        string? namePrefix = null,
        Action<string>? userCreated = null,
        CancellationToken cancellationToken = default)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.OracleDdl, namePrefix, userCreated, cancellationToken);

    public static async Task<OracleTestHarness> CreateFromDdlAsync(
        string adminConnectionString,
        string ddl,
        string? namePrefix = null,
        Action<string>? userCreated = null,
        CancellationToken cancellationToken = default)
    {
        var prefix = (namePrefix ?? "inquiry").ToUpperInvariant();
        var schemaUser = prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();
        var schemaPassword = "Pw_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var connectionString = new OracleConnectionStringBuilder(adminConnectionString)
        {
            UserID = schemaUser,
            Password = schemaPassword,
        }.ToString();
        var createWasAttempted = false;

        try
        {
            await using (var admin = new OracleConnection(adminConnectionString))
            {
                await admin.OpenAsync(cancellationToken);
                await using (var create = admin.CreateCommand())
                {
                    create.CommandText = $"CREATE USER {schemaUser} IDENTIFIED BY \"{schemaPassword}\"";
                    createWasAttempted = true;
                    await create.ExecuteNonQueryAsync(cancellationToken);
                    userCreated?.Invoke(schemaUser);
                }

                await using (var grant = admin.CreateCommand())
                {
                    grant.CommandText = $"GRANT CONNECT, RESOURCE, UNLIMITED TABLESPACE, CREATE VIEW TO {schemaUser}";
                    await grant.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var db = new OracleConnection(connectionString))
            {
                await db.OpenAsync(cancellationToken);
                // Oracle has no multi-statement batch; execute each CREATE separately.
                foreach (var statement in SplitStatements(ddl))
                {
                    await using var cmd = db.CreateCommand();
                    cmd.CommandText = statement;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            var services = new ServiceCollection()
                .AddInquiry(
                    typeof(CustomerStore).Assembly,
                    typeof(VersionedItemStore).Assembly,
                    typeof(OracleUnsupportedFixtureMarker).Assembly)
                .AddInquiryOracle(connectionString)
                .BuildServiceProvider();

            return new OracleTestHarness(adminConnectionString, schemaUser, connectionString, services);
        }
        catch (Exception setupFailure)
        {
            if (createWasAttempted)
            {
                var cleanupFailure = await TryDropUserAsync(adminConnectionString, connectionString, schemaUser);
                if (cleanupFailure is not null)
                {
                    setupFailure.Data["OracleTestHarness.CleanupException"] = cleanupFailure;
                }
            }

            throw;
        }
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Exception? serviceFailure = null;
        try
        {
            await Services.DisposeAsync();
        }
        catch (Exception ex)
        {
            serviceFailure = ex;
        }

        CleanupFailure = await TryDropUserAsync(_adminConnectionString, ConnectionString, _schemaUser);
        if (serviceFailure is not null)
        {
            if (CleanupFailure is not null)
            {
                serviceFailure.Data["OracleTestHarness.CleanupException"] = CleanupFailure;
            }

            throw serviceFailure;
        }

        if (CleanupFailure is not null && DockerRequirement.IsRequired())
        {
            throw new InvalidOperationException($"Failed to drop disposable Oracle schema {_schemaUser}.", CleanupFailure);
        }
    }

    private static async Task<Exception?> TryDropUserAsync(
        string adminConnectionString,
        string userConnectionString,
        string schemaUser)
    {
        var failures = new List<Exception>();
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await using var admin = new OracleConnection(adminConnectionString);
            await admin.OpenAsync(cleanupCts.Token);
            if (!await UserExistsAsync(admin, schemaUser, cleanupCts.Token))
            {
                return null;
            }

            while (true)
            {
                try
                {
                    using var userConnection = new OracleConnection(userConnectionString);
                    OracleConnection.ClearPool(userConnection);

                    await using var cmd = admin.CreateCommand();
                    cmd.CommandTimeout = 2;
                    cmd.CommandText = $"DROP USER {schemaUser} CASCADE";
                    await cmd.ExecuteNonQueryAsync(cleanupCts.Token);
                    break;
                }
                catch (OracleException ex) when (ex.Number == 1918)
                {
                    // Another cleanup path already removed this disposable user.
                    break;
                }
                catch (OracleException ex) when (ex.Number == 1940 && !cleanupCts.IsCancellationRequested)
                {
                    // ODP.NET can keep a just-disposed async connection visible for a short period.
                    // Retry only the documented "currently connected" teardown race within the bound.
                    await Task.Delay(100, cleanupCts.Token);
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures),
        };
    }

    private static async Task<bool> UserExistsAsync(
        OracleConnection admin,
        string schemaUser,
        CancellationToken cancellationToken)
    {
        await using var command = admin.CreateCommand();
        command.BindByName = true;
        command.CommandTimeout = 2;
        command.CommandText = "SELECT COUNT(*) FROM ALL_USERS WHERE USERNAME = :username";
        command.Parameters.Add("username", OracleDbType.Varchar2).Value = schemaUser;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }
}
