using Inquiry.Connections;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MySql.Tests;

public sealed class MySqlProviderIntegrationTests
{
    [Fact]
    public void MySqlProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryMySql("Server=localhost;Database=inquiry;User ID=root;Password=root")
            .BuildServiceProvider();

        Assert.IsType<MySqlInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }
}
