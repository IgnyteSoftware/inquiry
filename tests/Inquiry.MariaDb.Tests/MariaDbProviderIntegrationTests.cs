using Inquiry.Connections;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.Tests;

public sealed class MariaDbProviderIntegrationTests
{
    [Fact]
    public void MariaDbProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryMariaDb("Server=localhost;Database=inquiry;User ID=root;Password=root")
            .BuildServiceProvider();

        Assert.IsType<MariaDbInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }
}
