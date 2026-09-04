using Inquiry.Connections;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

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

    [Fact]
    public async Task MariaDbProviderOpensConnectionsFromRegisteredDataSource()
    {
        var dataSource = new MySqlDataSourceBuilder("Server=localhost;Database=inquiry;User ID=root;Password=root").Build();
        using var serviceProvider = new ServiceCollection()
            .AddInquiryMariaDb(dataSource)
            .BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IInquiryConnectionFactory>();

        await dataSource.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await factory.OpenConnectionAsync());
    }
}
