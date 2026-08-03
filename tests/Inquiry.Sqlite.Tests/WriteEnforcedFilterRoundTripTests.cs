using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end <c>[InquiryGlobalFilter(EnforceOnWrites = true)]</c> behaviour against real SQLite: a
/// key-based write aimed at another tenant's row affects zero rows and leaves the row byte-for-byte
/// intact, on every write shape (update, update-returning, soft delete, hard delete, restore, batch
/// delete). The positive controls matter as much as the negatives — an enforcement bug that blocked
/// every write would pass the negative assertions alone.
/// </summary>
public sealed class WriteEnforcedFilterRoundTripTests
{
    private const long TenantA = 1L;
    private const long TenantB = 2L;

    private static IDisposable Scope(long tenantId)
        => InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId });

    /// <summary>Seeds one row per tenant and returns (tenantA row id, tenantB row id).</summary>
    private static async Task<(long ARow, long BRow)> SeedAsync(WriteEnforcedDocStore store)
    {
        // Insert is never filtered, so seeding needs no ambient scope.
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = TenantA, Title = "A doc" });
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = TenantB, Title = "B doc" });

        using (Scope(TenantA))
        {
            var a = Assert.Single(await store.AllAsync());
            using (Scope(TenantB))
            {
                var b = Assert.Single(await store.AllAsync());
                return (a.Id, b.Id);
            }
        }
    }

    private static async Task<WriteEnforcedDoc> ReadAsync(WriteEnforcedDocStore store, long tenantId)
    {
        using (Scope(tenantId))
        {
            return Assert.Single(await store.AllAsync());
        }
    }

    [Fact]
    public async Task UpdateOfAnotherTenantsRowAffectsNoRowsAndLeavesItIntact()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (_, bRow) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            // Tenant A holds tenant B's key — the read filter cannot help here, only the write filter.
            var stolen = new WriteEnforcedDoc { Id = bRow, TenantId = TenantB, Title = "hijacked" };
            Assert.False(await store.UpdateAsync(stolen));
            Assert.Null(await store.UpdateReturningAsync(stolen));
        }

        var b = await ReadAsync(store, TenantB);
        Assert.Equal("B doc", b.Title);
    }

    [Fact]
    public async Task DeleteRestoreAndPurgeOfAnotherTenantsRowAffectNoRows()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (_, bRow) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            Assert.False(await store.DeleteAsync(bRow));
            Assert.False(await store.PurgeAsync(bRow));
            Assert.False(await store.RestoreAsync(bRow));
        }

        var b = await ReadAsync(store, TenantB);
        Assert.False(b.IsDeleted);
        Assert.Equal("B doc", b.Title);
    }

    [Fact]
    public async Task BatchDeleteSkipsAnotherTenantsKeys()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (aRow, bRow) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            // Both keys go in; only the caller's own row is affected.
            Assert.Equal(1, await store.DeleteAllAsync(new[] { aRow, bRow }));
        }

        var b = await ReadAsync(store, TenantB);
        Assert.False(b.IsDeleted);
        var a = await ReadAsync(store, TenantA);
        Assert.True(a.IsDeleted);
    }

    [Fact]
    public async Task WritesInsideTheOwningScopeStillSucceed()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (aRow, _) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            var mine = new WriteEnforcedDoc { Id = aRow, TenantId = TenantA, Title = "renamed" };
            Assert.True(await store.UpdateAsync(mine));

            var returned = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = aRow, TenantId = TenantA, Title = "renamed twice" });
            Assert.NotNull(returned);
            Assert.Equal("renamed twice", returned!.Title);

            Assert.True(await store.DeleteAsync(aRow));
            Assert.True(await store.RestoreAsync(aRow));
            Assert.True(await store.PurgeAsync(aRow));
        }

        using (Scope(TenantA))
        {
            Assert.Empty(await store.AllAsync());
        }
    }

    [Fact]
    public async Task OwnRowUpdateReturningSurvivesANoOpAndAFilterColumnChange()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (aRow, _) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            // A no-op write and a write that changes the enforced column itself must both come back
            // with the row. The emulated-returning dialects guard the read-back on the affected-row
            // count precisely so these two cases do not read as misses.
            var unchanged = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = aRow, TenantId = TenantA, Title = "A doc" });
            Assert.NotNull(unchanged);
            Assert.Equal("A doc", unchanged!.Title);

            var moved = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = aRow, TenantId = TenantB, Title = "moved" });
            Assert.NotNull(moved);
            Assert.Equal(TenantB, moved!.TenantId);
        }

        using (Scope(TenantA))
        {
            Assert.Empty(await store.AllAsync());
        }
    }

    [Fact]
    public async Task SetBasedUpdateAllAndHardPredicateDeleteStayInsideTheTenant()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (aRow, bRow) = await SeedAsync(store);

        using (Scope(TenantA))
        {
            var rewrite = new List<WriteEnforcedDoc>
            {
                new() { Id = aRow, TenantId = TenantA, Title = "bulk" },
                new() { Id = bRow, TenantId = TenantB, Title = "bulk" },
            };
            Assert.Equal(1, await store.UpdateAllAsync(rewrite));
            Assert.Equal(0, await store.PurgeByTitleAsync("B doc"));
        }

        var b = await ReadAsync(store, TenantB);
        Assert.Equal("B doc", b.Title);
    }

    [Fact]
    public async Task WriteWithoutAnAmbientScopeThrowsBeforeExecuting()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.WriteEnforcedDocSqliteDdl, "WriteEnforced");
        await using var _ = harness;
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();
        var (aRow, _) = await SeedAsync(store);

        // No scope: the missing-value exception, never a silent zero-row write that reads as
        // "someone else's row" and never an unfiltered one that reads as a security hole.
        await Assert.ThrowsAsync<InquiryFilterValueMissingException>(
            () => store.UpdateAsync(new WriteEnforcedDoc { Id = aRow, TenantId = TenantA, Title = "x" }));
        await Assert.ThrowsAsync<InquiryFilterValueMissingException>(() => store.DeleteAsync(aRow));
        await Assert.ThrowsAsync<InquiryFilterValueMissingException>(() => store.DeleteAllAsync(new[] { aRow }));

        var a = await ReadAsync(store, TenantA);
        Assert.Equal("A doc", a.Title);
    }
}
