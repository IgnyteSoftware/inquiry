using System;
using System.Collections.Generic;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class AuditTimestampIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public AuditTimestampIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertStampsBothTimestamps()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditTimestampOracleDdl, "audit");

        var store = harness.GetRequiredService<AuditDocStore>();

        var before = DateTime.UtcNow;
        var doc = (await store.InsertReturningAsync(new AuditDoc { Title = "First" }))!;
        var after = DateTime.UtcNow;

        Assert.InRange(doc.CreatedAt, before, after);
        Assert.InRange(doc.ModifiedAt, before, after);
    }

    [SkippableFact]
    public async Task SuppliedCreatedAtIsPreservedOnInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditTimestampOracleDdl, "audit");

        var store = harness.GetRequiredService<AuditDocStore>();

        var suppliedDate = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var doc = (await store.InsertReturningAsync(new AuditDoc { Title = "Backdated", CreatedAt = suppliedDate }))!;
        var reloaded = (await store.SelectByKeyAsync(doc.Id))!;

        Assert.Equal(suppliedDate, doc.CreatedAt);
        Assert.Equal(suppliedDate, reloaded.CreatedAt);
    }

    [SkippableFact]
    public async Task UpdateAdvancesModifiedAtAndCannotClobberCreatedAt()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditTimestampOracleDdl, "audit");

        var store = harness.GetRequiredService<AuditDocStore>();

        var doc = (await store.InsertReturningAsync(new AuditDoc { Title = "Original" }))!;
        var originalCreatedAt = doc.CreatedAt;

        await store.UpdateAsync(new AuditDoc { Id = doc.Id, Title = "Updated" });
        var reloaded = (await store.SelectByKeyAsync(doc.Id))!;

        Assert.Equal(originalCreatedAt, reloaded.CreatedAt);
        Assert.True(reloaded.ModifiedAt >= doc.ModifiedAt);
    }

    [SkippableFact]
    public async Task BatchUpdateStampsEachItem()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditTimestampOracleDdl, "audit");

        var store = harness.GetRequiredService<AuditDocStore>();

        var a = (await store.InsertReturningAsync(new AuditDoc { Title = "A" }))!;
        var b = (await store.InsertReturningAsync(new AuditDoc { Title = "B" }))!;

        var updated = await store.UpdateAllAsync(new List<AuditDoc>
        {
            new() { Id = a.Id, Title = "A2" },
            new() { Id = b.Id, Title = "B2" },
        });

        Assert.Equal(2, updated);

        var ra = (await store.SelectByKeyAsync(a.Id))!;
        var rb = (await store.SelectByKeyAsync(b.Id))!;

        Assert.True(ra.ModifiedAt >= a.ModifiedAt);
        Assert.True(rb.ModifiedAt >= b.ModifiedAt);
    }
}
