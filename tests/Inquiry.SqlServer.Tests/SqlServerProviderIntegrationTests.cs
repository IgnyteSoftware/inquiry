using Inquiry.Connections;
using Inquiry.Pipeline;
using Inquiry.Sql;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

public sealed class SqlServerProviderIntegrationTests
{
    [Fact]
    public void SqlServerProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquirySqlServer("Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true")
            .BuildServiceProvider();

        Assert.IsType<SqlServerInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.IsType<SqlServerInquirySqlDialect>(serviceProvider.GetRequiredService<InquirySqlDialect>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }
}
