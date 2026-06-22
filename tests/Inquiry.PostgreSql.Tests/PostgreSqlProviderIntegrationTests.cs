using Inquiry.Connections;
using Inquiry.Pipeline;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.Tests;

public sealed class PostgreSqlProviderIntegrationTests
{
    [Fact]
    public void PostgreSqlProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryPostgreSql("Host=localhost;Database=postgres;Username=postgres;Password=postgres")
            .BuildServiceProvider();

        Assert.IsType<PostgreSqlInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }

    [Fact]
    public void PostgreSqlFactoryAdvertisesPersistentPreparedStatements()
    {
        using var factory = new PostgreSqlInquiryConnectionFactory("Host=localhost;Database=postgres;Username=postgres;Password=postgres");

        // Npgsql keeps server-side prepared statements in a pool-level cache, so the capability
        // gate is true (SqlClient/SQLite default to false).
        Assert.True(((IInquiryConnectionFactory)factory).SupportsPersistentPreparedStatements);
    }

    [Fact]
    public async Task ServiceProviderDisposalDisposesTheDataSource()
    {
        var serviceProvider = new ServiceCollection()
            .AddInquiryPostgreSql("Host=localhost;Database=postgres;Username=postgres;Password=postgres")
            .BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IInquiryConnectionFactory>();

        // The factory is a DI singleton that owns its NpgsqlDataSource, so disposing the container must
        // dispose the data source with it.
        await serviceProvider.DisposeAsync();

        // Proof the data source was disposed: opening now fails fast with ObjectDisposedException from the
        // disposed data source — no connection attempt, so this holds without a live PostgreSQL.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await factory.OpenConnectionAsync());
    }

    [Fact]
    public async Task FactoryDisposalIsIdempotentAcrossSyncAndAsync()
    {
        var factory = new PostgreSqlInquiryConnectionFactory("Host=localhost;Database=postgres;Username=postgres;Password=postgres");

        await ((IAsyncDisposable)factory).DisposeAsync();
        ((IDisposable)factory).Dispose(); // second dispose via the other path must be a no-op, not a throw.
    }

    [Fact]
    public void FailoverStringEqualToPrimaryConstructsAndDisposesCleanly()
    {
        const string connectionString = "Host=localhost;Database=postgres;Username=postgres;Password=postgres";

        // A failover string identical to the primary is normalized to no-failover (no second data source
        // is built and the failover open-path is never taken). Construction and disposal must stay clean.
        using var factory = new PostgreSqlInquiryConnectionFactory(
            connectionString,
            new PostgreSqlInquiryOptions { FailoverConnectionString = connectionString });

        Assert.True(((IInquiryConnectionFactory)factory).SupportsPersistentPreparedStatements);
    }

    [Theory]
    [InlineData(PostgreSqlCompatibility.CockroachDb)]
    [InlineData(PostgreSqlCompatibility.AuroraPostgreSql)]
    public void OptionsOverloadRegistersFactory(PostgreSqlCompatibility compatibility)
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryPostgreSql(
                "Host=localhost;Database=postgres;Username=postgres;Password=postgres",
                o => o.Compatibility = compatibility)
            .BuildServiceProvider();

        Assert.IsType<PostgreSqlInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
    }
}
