using System;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace Inquiry.MySql.Tests.Fixtures;

/// <summary>Starts one MySQL container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class MySqlContainerFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            // MySqlBulkCopy ([InquiryBulkInsert]) streams rows via LOAD DATA LOCAL INFILE, which the
            // server rejects unless local_infile is enabled (off by default in MySQL 8.x).
            _container = new MySqlBuilder("mysql:8.4")
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
            SkipReason = "MySQL container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
