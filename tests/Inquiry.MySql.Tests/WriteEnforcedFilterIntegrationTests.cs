using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Write-side enforcement of <c>[InquiryGlobalFilter(EnforceOnWrites = true)]</c> against real
/// MySQL. Three things only a live engine settles: that a cross-tenant key write affects nothing
/// and returns nothing; that the emulated-returning read-back does NOT lose a legitimate write which
/// changes the filter column or changes nothing at all; and that the set-based UpdateAll and hard
/// predicate-delete statements stay inside the tenant.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class WriteEnforcedFilterIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public WriteEnforcedFilterIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private static IDisposable Scope(long tenantId)
        => InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId });

    private Task<MySqlTestHarness> CreateHarnessAsync()
        => MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.WriteEnforcedDocMySqlDdl, "wenf");

    [SkippableFact]
    public async Task CrossTenantKeyWritesAffectNoRowsAndReturnNoRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();

        // Insert is never filtered, so both tenants can be seeded without a scope.
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 1, Title = "A doc" });
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 2, Title = "B doc" });

        long otherTenantRow;
        using (Scope(2))
        {
            otherTenantRow = Assert.Single(await store.AllAsync()).Id;
        }

        using (Scope(1))
        {
            var stolen = new WriteEnforcedDoc { Id = otherTenantRow, TenantId = 2, Title = "hijacked" };
            Assert.False(await store.UpdateAsync(stolen));
            Assert.Null(await store.UpdateReturningAsync(stolen));
            Assert.False(await store.DeleteAsync(otherTenantRow));
            Assert.False(await store.PurgeAsync(otherTenantRow));
            Assert.False(await store.RestoreAsync(otherTenantRow));
            Assert.Equal(0, await store.DeleteAllAsync(new[] { otherTenantRow }));
        }

        using (Scope(2))
        {
            var untouched = Assert.Single(await store.AllAsync());
            Assert.Equal("B doc", untouched.Title);
            Assert.False(untouched.IsDeleted);
        }
    }

    [SkippableFact]
    public async Task OwnRowUpdateReturningSurvivesANoOpAndAFilterColumnChange()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();

        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 1, Title = "A doc" });
        long row;
        using (Scope(1))
        {
            row = Assert.Single(await store.AllAsync()).Id;
        }

        using (Scope(1))
        {
            // A no-op update changes no column. Where the read-back is emulated and guarded on the
            // affected-row count, that count is zero under changed-row semantics (MySQL family,
            // depending on CLIENT_FOUND_ROWS) — the row must still come back, not read as a miss.
            var unchanged = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = row, TenantId = 1, Title = "A doc" });
            Assert.NotNull(unchanged);
            Assert.Equal("A doc", unchanged!.Title);

            var renamed = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = row, TenantId = 1, Title = "renamed" });
            Assert.NotNull(renamed);
            Assert.Equal("renamed", renamed!.Title);

            // The write CHANGES the enforced column itself. A read-back that re-tested the term
            // against the POST-update value would return null for a write that succeeded.
            var moved = await store.UpdateReturningAsync(
                new WriteEnforcedDoc { Id = row, TenantId = 2, Title = "moved" });
            Assert.NotNull(moved);
            Assert.Equal(2, moved!.TenantId);
            Assert.Equal("moved", moved.Title);
        }

        // And the move really happened — the row is now the other tenant's.
        using (Scope(1))
        {
            Assert.Empty(await store.AllAsync());
        }

        using (Scope(2))
        {
            Assert.Equal("moved", Assert.Single(await store.AllAsync()).Title);
        }
    }

    [SkippableFact]
    public async Task SetBasedUpdateAllAndHardPredicateDeleteStayInsideTheTenant()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync();
        var store = harness.GetRequiredService<WriteEnforcedDocStore>();

        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 1, Title = "A1" });
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 1, Title = "A2" });
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 2, Title = "B1" });
        await store.InsertAsync(new WriteEnforcedDoc { TenantId = 2, Title = "B2" });

        List<WriteEnforcedDoc> everyRow;
        using (Scope(1))
        {
            everyRow = (await store.AllAsync()).ToList();
        }

        using (Scope(2))
        {
            everyRow.AddRange(await store.AllAsync());
        }

        using (Scope(1))
        {
            // Four distinct keys clears the runtime's two-item threshold, so the MySQL family takes the
            // set-based UPDATE ... JOIN route whose footer carries the alias-qualified enforced term,
            // and Oracle takes the array-bind route with the repeated filter array.
            var rewrite = everyRow
                .Select(static doc => new WriteEnforcedDoc { Id = doc.Id, TenantId = doc.TenantId, Title = "bulk" })
                .ToList();
            Assert.Equal(2, await store.UpdateAllAsync(rewrite));

            // A hard set-based delete drops the soft-delete activeness term but keeps the tenant term.
            Assert.Equal(0, await store.PurgeByTitleAsync("B1"));
        }

        using (Scope(2))
        {
            var titles = (await store.AllAsync()).Select(static doc => doc.Title).OrderBy(static t => t).ToArray();
            Assert.Equal(new[] { "B1", "B2" }, titles);

            // Positive control: inside its own scope the same delete works.
            Assert.Equal(1, await store.PurgeByTitleAsync("B1"));
            Assert.Equal("B2", Assert.Single(await store.AllAsync()).Title);
        }
    }
}
