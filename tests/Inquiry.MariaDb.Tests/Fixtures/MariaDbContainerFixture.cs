using System;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using MySqlConnector;
using Testcontainers.MariaDb;
using Xunit;

namespace Inquiry.MariaDb.Tests.Fixtures;

/// <summary>Starts one MariaDB container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class MariaDbContainerFixture : IAsyncLifetime
{
    private MariaDbContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            // MySqlBulkCopy ([InquiryBulkInsert]) streams rows via LOAD DATA LOCAL INFILE, which the
            // server rejects unless local_infile is enabled (switched on explicitly here).
            _container = new MariaDbBuilder("mariadb:11.4")
                .WithCommand("--local-infile=1")
                .Build();
            await _container.StartAsync();
            // The container's default user is a limited app account that cannot CREATE/USE the
            // throwaway databases each harness provisions. Connect as root (Testcontainers sets the
            // root password to the same value as the app user) for full admin rights.
            AdminConnectionString = new MySqlConnectionStringBuilder(_container.GetConnectionString())
            {
                UserID = "root",
            }.ToString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "MariaDB container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
