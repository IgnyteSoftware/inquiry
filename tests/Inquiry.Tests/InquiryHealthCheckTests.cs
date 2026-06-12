using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Data.Common;

namespace Inquiry.Tests;

public sealed class InquiryHealthCheckTests
{
    [Fact]
    public async Task ReportsHealthyWhenConnectionOpens()
    {
        var check = new InquiryHealthCheck(new HealthTestConnectionFactory(failOpen: false));

        var result = await check.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReportsFailureStatusWhenConnectionFailsToOpen()
    {
        var check = new InquiryHealthCheck(new HealthTestConnectionFactory(failOpen: true));

        var result = await check.CheckHealthAsync(CreateContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task AddInquiryRegistersHealthCheckOverConnectionFactory()
    {
        var services = new ServiceCollection();
        // HealthCheckService requires ILogger<>; the null logger keeps the test free of the full logging package.
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddSingleton<IInquiryConnectionFactory>(new HealthTestConnectionFactory(failOpen: false));
        services.AddHealthChecks().AddInquiry(tags: new[] { "ready" });

        await using var provider = services.BuildServiceProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();
        var report = await healthService.CheckHealthAsync();

        var entry = Assert.Single(report.Entries);
        Assert.Equal("inquiry", entry.Key);
        Assert.Equal(HealthStatus.Healthy, entry.Value.Status);
        Assert.Contains("ready", entry.Value.Tags);
    }

    private static HealthCheckContext CreateContext() => new()
    {
        Registration = new HealthCheckRegistration(
            "inquiry",
            _ => throw new InvalidOperationException("not used"),
            failureStatus: null,
            tags: null),
    };

    private sealed class HealthTestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly bool _failOpen;

        public HealthTestConnectionFactory(bool failOpen) => _failOpen = failOpen;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_failOpen)
            {
                throw new InvalidOperationException("Server unreachable.");
            }

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
