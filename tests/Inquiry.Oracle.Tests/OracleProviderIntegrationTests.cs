using Inquiry.Connections;
using Inquiry.Oracle.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Oracle.Tests;

public sealed class OracleProviderIntegrationTests
{
    [Fact]
    public void OracleProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryOracle("User Id=inquiry;Password=inquiry;Data Source=localhost:1521/FREEPDB1")
            .BuildServiceProvider();

        Assert.IsType<OracleInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }
}
