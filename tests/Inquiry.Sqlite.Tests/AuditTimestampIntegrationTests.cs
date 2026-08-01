using System;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Auditing timestamps end-to-end: insert stamps both columns (caller-observable), update advances
/// ModifiedAt while the stored CreatedAt survives — even when the updated entity instance carries a
/// default CreatedAt, proving the column is excluded from the UPDATE SET rather than re-written.
/// </summary>
public sealed class AuditTimestampIntegrationTests
{
    [Fact]
    public async Task InsertStampsBothTimestamps()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditTimestampSqliteDdl, "Audit");
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
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditTimestampSqliteDdl, "Audit");
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
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditTimestampSqliteDdl, "Audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var stored = (await store.InsertReturningAsync(new AuditDoc { Title = "v1" }))!;

        var reconstructed = new AuditDoc { Id = stored.Id, Title = "v2" };
        await Task.Delay(15);
        Assert.True(await store.UpdateAsync(reconstructed));

        var after = (await store.SelectByKeyAsync(stored.Id))!;
        Assert.Equal("v2", after.Title);
        Assert.Equal(stored.CreatedAt, after.CreatedAt);
        Assert.True(after.ModifiedAt > stored.ModifiedAt);

        Assert.Equal(after.ModifiedAt, reconstructed.ModifiedAt);
    }

    [Fact]
    public async Task BatchUpdateStampsEachItem()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditTimestampSqliteDdl, "Audit");
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
