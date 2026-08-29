using Inquiry.Connections;
using Inquiry.Pipeline;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

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
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }

    [Fact]
    public void OptionsOverloadRegistersFactory()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquirySqlServer(
                "Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true",
                o => o.Compatibility = SqlServerCompatibility.AzureSql)
            .BuildServiceProvider();

        Assert.IsType<SqlServerInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
    }

    [Fact]
    public async Task DataSourceOverloadUsesExternallyOwnedDataSource()
    {
        await using var dataSource = new RecordingDataSource();
        await using var serviceProvider = new ServiceCollection()
            .AddInquirySqlServer(dataSource)
            .BuildServiceProvider();

        var factory = serviceProvider.GetRequiredService<IInquiryConnectionFactory>();
        await using var connection = await factory.OpenConnectionAsync();

        Assert.True(dataSource.Opened);
        Assert.IsType<RecordingConnection>(connection);
    }

    private sealed class RecordingDataSource : DbDataSource
    {
        public bool Opened { get; private set; }

        public override string ConnectionString => "recording";

        protected override DbConnection CreateDbConnection() => new RecordingConnection();

        protected override ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default)
        {
            Opened = true;
            return ValueTask.FromResult<DbConnection>(new RecordingConnection());
        }
    }

    private sealed class RecordingConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
