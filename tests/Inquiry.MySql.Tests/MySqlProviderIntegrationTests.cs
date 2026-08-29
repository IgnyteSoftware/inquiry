using Inquiry.Connections;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

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

    [Fact]
    public async Task MySqlProviderOpensConnectionsFromRegisteredDataSource()
    {
        var dataSource = new MySqlDataSourceBuilder("Server=localhost;Database=inquiry;User ID=root;Password=root").Build();
        using var serviceProvider = new ServiceCollection()
            .AddInquiryMySql(dataSource)
            .BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IInquiryConnectionFactory>();

        await dataSource.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await factory.OpenConnectionAsync());
    }
}
