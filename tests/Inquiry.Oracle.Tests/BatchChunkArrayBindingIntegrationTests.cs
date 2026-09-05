using System.Data;
using System.Data.Common;
using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Inquiry.Tests.Shared;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("BatchChunkItem")]
public sealed class BatchChunkItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 100)]
    public string ValueText { get; set; } = string.Empty;
}

public partial class BatchChunkItemStore : InquiryStore<BatchChunkItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<int> UpdateAllAsync(IEnumerable<BatchChunkItem> items, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}

[Collection(OracleCollection.Name)]
public sealed class BatchChunkArrayBindingIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE BatchChunkItem (
            Id NUMBER(10) NOT NULL PRIMARY KEY,
            ValueText VARCHAR2(100) NOT NULL
        )
        """;

    private readonly OracleContainerFixture _fixture;

    public BatchChunkArrayBindingIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FiveItemMutationsUseTwoTwoOneArrayBindingChunks()
    {
        await using var context = await CreateContextAsync("batch_chunk", maxBatchSize: 2);
        var inserted = CreateItems(5, "Inserted");

        Assert.Equal(5, await context.Store.InsertAllAsync(inserted));
        AssertChunkEvidence(context.Probe, "INSERT", inserted, [2, 2, 1]);
        AssertRows(inserted, await ReadAllAsync(context.Harness.ConnectionString));

        context.Probe.Reset();
        var updated = CreateItems(5, "Updated");
        Assert.Equal(5, await context.Store.UpdateAllAsync(updated));
        AssertChunkEvidence(context.Probe, "UPDATE", updated, [2, 2, 1]);
        AssertRows(updated, await ReadAllAsync(context.Harness.ConnectionString));

        context.Probe.Reset();
        var ids = updated.Select(static item => item.Id).ToArray();
        Assert.Equal(5, await context.Store.DeleteAllAsync(ids));
        Assert.Empty(context.Probe.InitializedChunkSizes);
        var deleteCommand = Assert.Single(context.Probe.FinalizedCommands);
        Assert.Contains("DELETE", deleteCommand.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON_TABLE", deleteCommand.CommandText, StringComparison.OrdinalIgnoreCase);
        var deleteEvidence = Assert.IsType<OracleCommandEvidence>(deleteCommand.Metadata);
        Assert.Equal(0, deleteEvidence.ArrayBindCount);
        Assert.False(Assert.Single(deleteEvidence.Parameters).ValueIsObjectArray);
        Assert.Empty(await ReadAllAsync(context.Harness.ConnectionString));
    }

    [Fact]
    public async Task DuplicateKeyInLaterChunkRollsBackEarlierChunk()
    {
        await using var context = await CreateContextAsync("batch_dup", maxBatchSize: 2);
        var items = new[]
        {
            Item(1, "First"),
            Item(2, "Second"),
            Item(3, "Third"),
            Item(1, "Duplicate"),
        };

        await Assert.ThrowsAsync<OracleException>(() => context.Store.InsertAllAsync(items));

        Assert.Equal(new[] { 2, 2 }, context.Probe.InitializedChunkSizes);
        Assert.Equal(2, context.Probe.FinalizedCommands.Count);
        Assert.Equal(1, context.Probe.BeginTransactionCount);
        Assert.Empty(await ReadAllAsync(context.Harness.ConnectionString));
    }

    [Fact]
    public async Task AmbientOuterRollbackOwnsTheOnlyPhysicalTransaction()
    {
        await using var context = await CreateContextAsync("batch_ambient", maxBatchSize: 2);
        var inquiry = context.Harness.GetRequiredService<IInquiry>();

        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            Assert.Equal(5, await context.Store.InsertAllAsync(CreateItems(5, "Ambient")));
            Assert.Equal(new[] { 2, 2, 1 }, context.Probe.InitializedChunkSizes);
            Assert.Equal(1, context.Probe.BeginTransactionCount);
        }

        Assert.Equal(1, context.Probe.BeginTransactionCount);
        Assert.Empty(await ReadAllAsync(context.Harness.ConnectionString));
    }

    [Fact]
    public async Task CancellationWhileFillingSecondChunkDisposesOnceAndRollsBackFirstChunk()
    {
        await using var context = await CreateContextAsync("batch_cancel", maxBatchSize: 2);
        using var cancellation = new CancellationTokenSource();
        var source = new SinglePassCancellingEnumerable<BatchChunkItem>(
            CreateItems(5, "Cancelled"),
            cancellation,
            cancelAtMoveNext: 3);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Store.InsertAllAsync(source, cancellation.Token));

        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(3, source.MoveNextCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(new[] { 2 }, context.Probe.InitializedChunkSizes);
        Assert.Single(context.Probe.FinalizedCommands);
        Assert.Equal(1, context.Probe.BeginTransactionCount);
        Assert.Empty(await ReadAllAsync(context.Harness.ConnectionString));
    }

    [Fact]
    public async Task ThousandAndOneItemsSplitIntoOneThousandAndOne()
    {
        await using var context = await CreateContextAsync("batch_1001", maxBatchSize: null);
        var items = CreateItems(1001, "Large");

        Assert.Equal(1001, await context.Store.InsertAllAsync(items));

        AssertChunkEvidence(context.Probe, "INSERT", items, [1000, 1]);
        AssertRows(items, await ReadAllAsync(context.Harness.ConnectionString));
    }

    private async Task<TestContext> CreateContextAsync(string prefix, int? maxBatchSize)
    {
        Assert.True(_fixture.IsAvailable, _fixture.SkipReason);
        var probe = new BatchExecutionProbe(SnapshotCommand);
        var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            prefix,
            configureOptions: maxBatchSize is null ? null : options => options.MaxBatchSize = maxBatchSize.Value,
            configureServices: probe.Decorate);
        return new TestContext(harness, harness.GetRequiredService<BatchChunkItemStore>(), probe);
    }

    private static void AssertChunkEvidence(
        BatchExecutionProbe probe,
        string operation,
        IReadOnlyList<BatchChunkItem> items,
        IReadOnlyList<int> chunkSizes)
    {
        Assert.Equal(chunkSizes, probe.InitializedChunkSizes);
        Assert.Equal(chunkSizes.Count, probe.FinalizedCommands.Count);
        Assert.Equal(1, probe.BeginTransactionCount);
        Assert.Equal(0, probe.CreateBatchCount);

        var offset = 0;
        for (var chunkIndex = 0; chunkIndex < chunkSizes.Count; chunkIndex++)
        {
            var command = probe.FinalizedCommands[chunkIndex];
            Assert.Contains(operation, command.CommandText, StringComparison.OrdinalIgnoreCase);
            var evidence = Assert.IsType<OracleCommandEvidence>(command.Metadata);
            var chunkSize = chunkSizes[chunkIndex];
            Assert.Equal(chunkSize, evidence.ArrayBindCount);

            var id = Assert.Single(evidence.Parameters, static parameter =>
                parameter.OracleDbType == OracleDbType.Int32);
            Assert.StartsWith("iq1$", id.Name, StringComparison.Ordinal);
            Assert.Equal(OracleDbType.Int32, id.OracleDbType);
            Assert.Equal(DbType.Int32, id.DbType);
            Assert.Equal(0, id.Size);
            Assert.True(id.ValueIsObjectArray);
            Assert.Equal(items.Skip(offset).Take(chunkSize).Select(static item => (object?)item.Id), id.Values);
            Assert.Null(id.ArrayBindSize);

            var text = Assert.Single(evidence.Parameters, static parameter =>
                parameter.OracleDbType == OracleDbType.Varchar2);
            Assert.StartsWith("iq1$", text.Name, StringComparison.Ordinal);
            Assert.Equal(OracleDbType.Varchar2, text.OracleDbType);
            Assert.Equal(DbType.String, text.DbType);
            Assert.Equal(0, text.Size);
            Assert.True(text.ValueIsObjectArray);
            var expectedText = items.Skip(offset).Take(chunkSize).Select(static item => (object?)item.ValueText).ToArray();
            Assert.Equal(expectedText, text.Values);
            Assert.Equal(expectedText.Select(static value => ((string)value!).Length), text.ArrayBindSize);
            offset += chunkSize;
        }
    }

    private static OracleCommandEvidence SnapshotCommand(DbCommand command)
    {
        var oracleCommand = Assert.IsType<OracleCommand>(command);
        var parameters = oracleCommand.Parameters
            .Cast<OracleParameter>()
            .Select(static parameter =>
            {
                var values = parameter.Value as object?[];
                return new OracleParameterEvidence(
                    parameter.ParameterName,
                    parameter.OracleDbType,
                    parameter.DbType,
                    parameter.Size,
                    values is not null,
                    values?.ToArray() ?? [],
                    parameter.ArrayBindSize?.ToArray());
            })
            .ToArray();
        return new OracleCommandEvidence(oracleCommand.ArrayBindCount, parameters);
    }

    private static BatchChunkItem[] CreateItems(int count, string prefix)
        => Enumerable.Range(1, count).Select(id => Item(id, $"{prefix} {id}")).ToArray();

    private static BatchChunkItem Item(int id, string value) => new() { Id = id, ValueText = value };

    private static void AssertRows(
        IEnumerable<BatchChunkItem> expected,
        IEnumerable<BatchChunkItem> actual)
        => Assert.Equal(
            expected.Select(static item => (item.Id, item.ValueText)),
            actual.Select(static item => (item.Id, item.ValueText)));

    private static async Task<BatchChunkItem[]> ReadAllAsync(string connectionString)
    {
        var items = new List<BatchChunkItem>();
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ValueText FROM BatchChunkItem ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(Item(Convert.ToInt32(reader.GetValue(0)), reader.GetString(1)));
        }

        return items.ToArray();
    }

    private sealed record OracleCommandEvidence(
        int ArrayBindCount,
        IReadOnlyList<OracleParameterEvidence> Parameters);

    private sealed record OracleParameterEvidence(
        string Name,
        OracleDbType OracleDbType,
        DbType DbType,
        int Size,
        bool ValueIsObjectArray,
        IReadOnlyList<object?> Values,
        IReadOnlyList<int>? ArrayBindSize);

    private sealed record TestContext(
        OracleTestHarness Harness,
        BatchChunkItemStore Store,
        BatchExecutionProbe Probe) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }
}
