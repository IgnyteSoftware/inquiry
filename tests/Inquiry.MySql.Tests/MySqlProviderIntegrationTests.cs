using System.Net;
using System.Net.Sockets;
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

    [Fact]
    public async Task BulkCopyOpensFromAnExternalDataSourceThatAllowsLocalInfile()
    {
        var dataSource = new MySqlDataSourceBuilder("Server=localhost;Database=inquiry;User ID=root;AllowLoadLocalInfile=true").Build();
        var factory = new MySqlInquiryConnectionFactory(dataSource);

        await dataSource.DisposeAsync();

        // The disposed data source proves that the open went through it. A password provider or a
        // certificate callback lives on the data source, not in its connection string.
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await factory.OpenBulkCopyConnectionAsync());
    }

    [Fact]
    public async Task BulkCopyOpenRetriesWithoutAFailoverConnectionString()
    {
        // A listener that drops every connection makes each open fail, and counts the attempts.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var attempts = 0;
        using var stop = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                Interlocked.Increment(ref attempts);
            }
        });
        var factory = new MySqlInquiryConnectionFactory(
            $"Server=127.0.0.1;Port={port};User ID=root;Password=root;Pooling=false;ConnectionTimeout=5",
            new MySqlInquiryOptions
            {
                Compatibility = MySqlCompatibility.CloudHosted,
                MaxAttempts = 3,
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
                RetryMaxDelay = TimeSpan.FromMilliseconds(1),
            });

        await Assert.ThrowsAsync<MySqlException>(async () => await factory.OpenBulkCopyConnectionAsync());

        Assert.Equal(3, Volatile.Read(ref attempts));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accepting);
    }
}
