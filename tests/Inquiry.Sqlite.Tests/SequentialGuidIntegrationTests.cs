using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

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
/// <c>[InquiryKey(SequentialGuid = true)]</c> end-to-end: unset keys get a v7 GUID the caller
/// observes (and can round-trip by key); supplied keys are never overwritten; batch insert
/// assigns per item.
/// </summary>
public sealed class SequentialGuidIntegrationTests
{
    private const string Ddl = "CREATE TABLE SeqDoc (Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL);";

    [Fact]
    public async Task UnsetKeyIsAssignedAndRoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SeqGuid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var doc = new SeqDoc { Title = "first" };
        Assert.Equal(Guid.Empty, doc.Id);

        await store.InsertAsync(doc);

        // The caller observes the generated key, and it is a version-7 GUID. (Byte inspection:
        // Guid.Version is .NET 9+ only and this suite also runs on net8.0.)
        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.Equal(0x70, doc.Id.ToByteArray(bigEndian: true)[6] & 0xF0);

        var loaded = await store.SelectByKeyAsync(doc.Id);
        Assert.NotNull(loaded);
        Assert.Equal("first", loaded!.Title);
    }

    [Fact]
    public async Task SuppliedKeyIsNotOverwritten()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SeqGuid");
        var store = harness.GetRequiredService<SeqDocStore>();

        var supplied = Guid.NewGuid();
        var doc = new SeqDoc { Id = supplied, Title = "explicit" };

        await store.InsertAsync(doc);

        Assert.Equal(supplied, doc.Id);
        Assert.NotNull(await store.SelectByKeyAsync(supplied));
    }

    [Fact]
    public async Task UpsertAssignsKeyWhenUnset()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SeqGuid");
        var store = harness.GetRequiredService<SeqDocStore>();

        // An unset key gets a fresh v7 before the upsert, making it an insert of a new row.
        var doc = new SeqDoc { Title = "upserted" };
        await store.UpsertAsync(doc);

        Assert.NotEqual(Guid.Empty, doc.Id);
        Assert.NotNull(await store.SelectByKeyAsync(doc.Id));

        // Upserting again with the now-set key updates in place rather than inserting.
        doc.Title = "updated";
        await store.UpsertAsync(doc);
        Assert.Equal("updated", (await store.SelectByKeyAsync(doc.Id))!.Title);
        Assert.Single(await store.SelectAllAsync());
    }

    [Fact]
    public async Task BatchInsertAssignsEachUnsetKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SeqGuid");
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
