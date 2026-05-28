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
}
