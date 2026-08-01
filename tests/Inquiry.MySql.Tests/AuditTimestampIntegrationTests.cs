using System;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Auditing timestamps end-to-end against real MySQL: insert stamps both columns (caller-observable),
/// update advances ModifiedAt while the stored CreatedAt survives — even when the updated entity
/// instance carries a default CreatedAt, proving the column is excluded from the UPDATE SET rather
/// than re-written.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class AuditTimestampIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public AuditTimestampIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertStampsBothTimestamps()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditTimestampMySqlDdl, "audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var doc = new AuditDoc { Title = "spec" };
        await store.InsertAsync(doc);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(doc.CreatedAt, before, after);
        Assert.InRange(doc.ModifiedAt, before, after);
    }

    [SkippableFact]
    public async Task SuppliedCreatedAtIsPreservedOnInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditTimestampMySqlDdl, "audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var imported = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var doc = new AuditDoc { Title = "imported", CreatedAt = imported };
        var inserted = (await store.InsertReturningAsync(doc))!;

        Assert.Equal(imported, doc.CreatedAt);
        Assert.Equal(imported, (await store.SelectByKeyAsync(inserted.Id))!.CreatedAt);
    }

    [SkippableFact]
    public async Task UpdateAdvancesModifiedAtAndCannotClobberCreatedAt()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditTimestampMySqlDdl, "audit");
        var store = harness.GetRequiredService<AuditDocStore>();

        var stored = (await store.InsertReturningAsync(new AuditDoc { Title = "v1" }))!;

        var reconstructed = new AuditDoc { Id = stored.Id, Title = "v2" };
        await Task.Delay(15);
        Assert.True(await store.UpdateAsync(reconstructed));

        var after = (await store.SelectByKeyAsync(stored.Id))!;
        Assert.Equal("v2", after.Title);
        Assert.Equal(stored.CreatedAt, after.CreatedAt);
        Assert.True(after.ModifiedAt > stored.ModifiedAt);

        Assert.Equal(reconstructed.ModifiedAt.Ticks - (reconstructed.ModifiedAt.Ticks % 10), after.ModifiedAt.Ticks);
        Assert.Equal(DateTimeKind.Utc, reconstructed.ModifiedAt.Kind);
    }

    [SkippableFact]
    public async Task BatchUpdateStampsEachItem()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditTimestampMySqlDdl, "audit");
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
