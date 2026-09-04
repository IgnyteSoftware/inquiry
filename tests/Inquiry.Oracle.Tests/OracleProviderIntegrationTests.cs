using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Oracle.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using System.Reflection;

namespace Inquiry.Oracle.Tests;

public sealed class OracleProviderIntegrationTests
{
    [Fact]
    public void OracleProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquiryOracle("User Id=inquiry;Password=inquiry;Data Source=localhost:1521/FREEPDB1")
            .BuildServiceProvider();

        var factory = Assert.IsType<OracleInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.Equal(InquiryBatchExecutionMode.ArrayBinding, factory.BatchExecutionMode);
        Assert.False(factory.SupportsBatchExecution);
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }

    [Fact]
    public async Task DataSourceOverloadUsesSuppliedDataSource()
    {
        await using var dataSource = new RecordingDbDataSource();
        await using var serviceProvider = new ServiceCollection()
            .AddInquiryOracle(dataSource)
            .BuildServiceProvider();

        var factory = serviceProvider.GetRequiredService<IInquiryConnectionFactory>();
        await using var connection = await factory.OpenConnectionAsync();

        Assert.True(dataSource.Opened);
    }

    [Fact]
    public void GeneratedInsertAllChunkBinderSetsOracleArrayMetadataAndValues()
    {
        var descriptorField = typeof(ProviderReadAllTypesStore)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(static field => field.Name.Contains("_batch_InsertAllAsync_", StringComparison.Ordinal));
        var descriptor = Assert.IsType<InquiryBatchCommand<ProviderReadAllTypes>>(descriptorField.GetValue(null));
        Assert.NotNull(descriptor.BindChunk);

        var firstToken = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var secondToken = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");
        var items = new[]
        {
            CreateItem(11, true, firstToken, "éclair", [1, 2]),
            CreateItem(12, false, secondToken, "東京", [3, 4, 5]),
        };

        using var command = new OracleCommand();
        descriptor.BindChunk!(command, items);

        Assert.Equal(2, command.ArrayBindCount);
        var parameters = command.Parameters.Cast<OracleParameter>().ToArray();
        Assert.Equal(11, parameters.Length);
        var name = parameters[9];
        Assert.Equal([6, 2], name.ArrayBindSize);
        Assert.Equal("éclair", Assert.IsType<object?[]>(name.Value)[0]);
        Assert.Equal("東京", Assert.IsType<object?[]>(name.Value)[1]);
        var payload = parameters[8];
        Assert.Equal([2, 3], payload.ArrayBindSize);
        Assert.Equal(new byte[] { 1, 2 }, Assert.IsType<byte[]>(Assert.IsType<object?[]>(payload.Value)[0]));
        Assert.Equal(new byte[] { 3, 4, 5 }, Assert.IsType<byte[]>(Assert.IsType<object?[]>(payload.Value)[1]));
        var enabled = parameters[2];
        Assert.Equal(System.Data.DbType.Int32, enabled.DbType);
        Assert.True(Assert.IsType<bool>(Assert.IsType<object?[]>(enabled.Value)[0]));
        Assert.False(Assert.IsType<bool>(Assert.IsType<object?[]>(enabled.Value)[1]));
        Assert.Null(enabled.ArrayBindSize);
        var token = parameters[3];
        Assert.Equal(System.Data.DbType.Binary, token.DbType);
        Assert.Equal(firstToken, Assert.IsType<Guid>(Assert.IsType<object?[]>(token.Value)[0]));
        Assert.Equal(secondToken, Assert.IsType<Guid>(Assert.IsType<object?[]>(token.Value)[1]));
        Assert.Null(token.ArrayBindSize);
    }

    private static ProviderReadAllTypes CreateItem(int id, bool enabled, Guid token, string name, byte[] payload) => new()
    {
        Id = id,
        NumberValue = id,
        Enabled = enabled,
        Token = token,
        ConvertedToken = new OracleExternalId(token),
        ConvertedEnabled = new OracleToggle(enabled),
        Payload = payload,
        Name = name,
        OccurredAt = new DateTime(2026, 7, 14, 12, 0, 0),
    };
}
