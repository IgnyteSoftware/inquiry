using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace Inquiry.PostgreSql.Tests.Fixtures;

/// <summary>Starts one PostgreSQL container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "PostgreSQL container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
