using System;
using System.Threading.Tasks;
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
            _container = new MySqlBuilder().WithImage("mysql:8.4").Build();
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
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
