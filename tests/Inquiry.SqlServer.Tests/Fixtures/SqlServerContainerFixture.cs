using System;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Testcontainers.MsSql;
using Xunit;

namespace Inquiry.SqlServer.Tests.Fixtures;

/// <summary>Starts one SQL Server container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            // Testcontainers.MsSql 4.x obsoleted the parameterless builder ctor; pass the image
            // explicitly. This is 4.12's own default tag (SQL Server 2022), pinned for determinism.
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
