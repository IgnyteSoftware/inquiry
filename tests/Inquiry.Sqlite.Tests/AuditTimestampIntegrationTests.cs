using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("AuditDoc")]
public sealed class AuditDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Title { get; set; } = string.Empty;

    [InquiryCreatedAt]
    public DateTime CreatedAt { get; set; }

    [InquiryModifiedAt]
    public DateTime ModifiedAt { get; set; }
}

public partial class AuditDocStore : InquiryStore<AuditDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryInsert(ReturnEntity = true)]
    public partial Task<AuditDoc?> InsertReturningAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(AuditDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<AuditDoc> docs, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<AuditDoc?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Auditing timestamps end-to-end: insert stamps both columns (caller-observable), update advances
/// ModifiedAt while the stored CreatedAt survives — even when the updated entity instance carries a
/// default CreatedAt, proving the column is excluded from the UPDATE SET rather than re-written.
/// </summary>
public sealed class AuditTimestampIntegrationTests
{
    private const string Ddl = "CREATE TABLE AuditDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, CreatedAt TEXT NOT NULL, ModifiedAt TEXT NOT NULL);";

    [Fact]
    public async Task InsertStampsBothTimestamps()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var doc = new AuditDoc { Title = "spec" };
        await store.InsertAsync(doc);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(doc.CreatedAt, before, after);
        Assert.InRange(doc.ModifiedAt, before, after);
    }

    [Fact]
    public async Task SuppliedCreatedAtIsPreservedOnInsert()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var imported = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var doc = new AuditDoc { Title = "imported", CreatedAt = imported };
        var inserted = (await store.InsertReturningAsync(doc))!;

        Assert.Equal(imported, doc.CreatedAt);
        Assert.Equal(imported, (await store.SelectByKeyAsync(inserted.Id))!.CreatedAt);
    }

    [Fact]
    public async Task UpdateAdvancesModifiedAtAndCannotClobberCreatedAt()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var stored = (await store.InsertReturningAsync(new AuditDoc { Title = "v1" }))!;

        // The hostile case: update via a *constructed* entity whose CreatedAt is default.
        // Because CreatedAt is excluded from the UPDATE SET, the stored value must survive.
        var reconstructed = new AuditDoc { Id = stored.Id, Title = "v2" };
        await Task.Delay(15);
        Assert.True(await store.UpdateAsync(reconstructed));

        var after = (await store.SelectByKeyAsync(stored.Id))!;
        Assert.Equal("v2", after.Title);
        Assert.Equal(stored.CreatedAt, after.CreatedAt);
        Assert.True(after.ModifiedAt > stored.ModifiedAt);

        // The caller observes the stamped ModifiedAt on the entity it passed in.
        Assert.Equal(after.ModifiedAt, reconstructed.ModifiedAt);
    }

    [Fact]
    public async Task BatchUpdateStampsEachItem()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var a = (await store.InsertReturningAsync(new AuditDoc { Title = "a" }))!;
        var b = (await store.InsertReturningAsync(new AuditDoc { Title = "b" }))!;
        var originalA = (await store.SelectByKeyAsync(a.Id))!;

        await Task.Delay(15);
        a.Title = "a2";
        b.Title = "b2";
        var affected = await store.UpdateAllAsync(new[] { a, b });
        Assert.Equal(2, affected);

        var afterA = (await store.SelectByKeyAsync(a.Id))!;
        Assert.Equal("a2", afterA.Title);
        Assert.Equal(originalA.CreatedAt, afterA.CreatedAt);
        Assert.True(afterA.ModifiedAt > originalA.ModifiedAt);
    }
}
