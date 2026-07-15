using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("SeqDoc")]
public sealed class SeqDoc
{
    [InquiryKey(SequentialGuid = true)]
    public Guid Id { get; set; }

    [InquiryColumn]
    public string Title { get; set; } = string.Empty;
}

public partial class SeqDocStore : InquiryStore<SeqDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SeqDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<SeqDoc> docs, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(SeqDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<SeqDoc?> SelectByKeyAsync(Guid id, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SeqDoc>> SelectAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>[InquiryKey(SequentialGuid = true)]</c> end-to-end: unset keys get a SQL Server-sequential
/// GUID the caller observes (and can round-trip by key); supplied keys are never overwritten;
/// batch insert assigns per item.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SequentialGuidIntegrationTests
{
    private const string Ddl = "CREATE TABLE [SeqDoc] ([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [Title] NVARCHAR(MAX) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;
    public SequentialGuidIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UnsetKeyIsAssignedAndRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "seqguid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var doc = new SeqDoc { Title = "first" };
        Assert.Equal(Guid.Empty, doc.Id);

        await store.InsertAsync(doc);

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.Equal(0x80, doc.Id.ToByteArray(bigEndian: true)[6] & 0xF0);

        var loaded = await store.SelectByKeyAsync(doc.Id);
        Assert.NotNull(loaded);
        Assert.Equal("first", loaded!.Title);
    }

    [SkippableFact]
    public async Task SuppliedKeyIsNotOverwritten()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "seqguid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var supplied = Guid.NewGuid();
        var doc = new SeqDoc { Id = supplied, Title = "explicit" };

        await store.InsertAsync(doc);

        Assert.Equal(supplied, doc.Id);
        Assert.NotNull(await store.SelectByKeyAsync(supplied));
    }

    [SkippableFact]
    public async Task UpsertAssignsKeyWhenUnset()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "seqguid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var doc = new SeqDoc { Title = "upserted" };
        await store.UpsertAsync(doc);

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.NotNull(await store.SelectByKeyAsync(doc.Id));

        doc.Title = "updated";
        await store.UpsertAsync(doc);
        Assert.Equal("updated", (await store.SelectByKeyAsync(doc.Id))!.Title);
        Assert.Single(await store.SelectAllAsync());
    }

    [SkippableFact]
    public async Task InsertedKeysAreClusteredIndexFriendly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "seqguid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var doc = new SeqDoc { Title = $"doc-{i}" };
            await store.InsertAsync(doc);
            ids.Add(doc.Id);
            if (i % 5 == 0) await Task.Delay(15);
        }

        // Sort both sides by SqlGuid to use the same comparison SQL Server applies — avoids
        // relying on unordered SELECT returning clustered-index scan order.
        var clientOrdered = ids.OrderBy(id => new System.Data.SqlTypes.SqlGuid(id)).ToList();

        Assert.Equal(clientOrdered, ids);
    }

    [SkippableFact]
    public async Task BatchInsertAssignsEachUnsetKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "seqguid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var docs = new List<SeqDoc>
        {
            new() { Title = "a" },
            new() { Id = Guid.NewGuid(), Title = "b" },
            new() { Title = "c" },
        };

        var affected = await store.InsertAllAsync(docs);
        Assert.Equal(3, affected);

        Assert.All(docs, d => Assert.NotEqual(Guid.Empty, d.Id));
        Assert.Equal(3, docs.Select(d => d.Id).Distinct().Count());
        Assert.Equal(3, (await store.SelectAllAsync()).Count);
    }
}
