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
        var factory = new PostgreSqlInquiryConnectionFactory("Host=localhost;Database=postgres;Username=postgres;Password=postgres");

        // W4: Npgsql keeps server-side prepared statements in a pool-level cache, so the capability
        // gate is true (SqlClient/SQLite default to false).
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
