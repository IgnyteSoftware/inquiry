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
            _container = new OracleBuilder().WithImage("gvenzl/oracle-free:23-slim-faststart").Build();
            await _container.StartAsync();
            AdminConnectionString = new OracleConnectionStringBuilder(_container.GetConnectionString())
            {
                UserID = "SYSTEM",
            }.ToString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
