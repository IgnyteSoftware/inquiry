using System;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>Starts one Oracle container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
/// <remarks>
/// The container's default connection string authenticates as the limited <c>oracle</c> application
/// user (CONNECT + RESOURCE only), which cannot <c>CREATE USER</c>. The throwaway-schema harness needs
/// admin rights, so <see cref="AdminConnectionString"/> rewrites the user to <c>SYSTEM</c>; in the
/// gvenzl image the SYSTEM password equals the configured <c>ORACLE_PASSWORD</c> (the same password
/// Testcontainers sets for the app user), so only the user id needs swapping.
/// </remarks>
public sealed class OracleContainerFixture : IAsyncLifetime
{
    private OracleContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            // Use the XE image whose service name (XEPDB1) matches what Testcontainers.Oracle bakes into
            // its connection string. The oracle-free image serves FREEPDB1 instead, so its generated
            // connection string fails with ORA-12514 (service not known).
            _container = new OracleBuilder("gvenzl/oracle-xe:21-slim-faststart").Build();
            await _container.StartAsync();
            AdminConnectionString = new OracleConnectionStringBuilder(_container.GetConnectionString())
            {
                UserID = "SYSTEM",
            }.ToString();

            // The faststart image's listener accepts connections a moment before the database service is
            // registered, so an immediate SYSTEM connect can hit ORA-12514 ("listener does not currently
            // know of service"). Poll until the service is actually serving before declaring availability.
            await WaitForServiceAsync(AdminConnectionString);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;
        }
    }

    private static async Task WaitForServiceAsync(string connectionString)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new OracleConnection(connectionString);
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM dual";
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch (OracleException) when (attempt < maxAttempts)
            {
                await Task.Delay(2000);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
