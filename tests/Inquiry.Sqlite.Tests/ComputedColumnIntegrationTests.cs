using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Server-computed column end-to-end against SQLite: the database computes <c>FullName</c> from the
/// stored DDL expression; insert/update never write it, and reads materialize the computed value.
/// </summary>
public sealed class ComputedColumnIntegrationTests
{
    [Fact]
    public async Task ComputedValueIsCalculatedByDatabaseOnInsert()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ComputedColumnSqliteDdl, "Computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var inserted = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace", BaseValue = 2, MixedCaseValue = 3 }))!;
        Assert.Equal("Ada Lovelace", inserted.FullName);
        Assert.Equal(5, inserted.ComputedTotal);

        var loaded = (await store.SelectByKeyAsync(inserted.Id))!;
        Assert.Equal("Ada Lovelace", loaded.FullName);
    }

    [Fact]
    public async Task ComputedValueTracksUpdatesToSourceColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ComputedColumnSqliteDdl, "Computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var doc = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper", BaseValue = 4, MixedCaseValue = 5 }))!;

        Assert.True(await store.UpdateAsync(new ComputedPerson { Id = doc.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored", BaseValue = 10, MixedCaseValue = 7 }));

        var after = (await store.SelectByKeyAsync(doc.Id))!;
        Assert.Equal("Grace Murray", after.FullName);
        Assert.Equal(17, after.ComputedTotal);
    }
}
