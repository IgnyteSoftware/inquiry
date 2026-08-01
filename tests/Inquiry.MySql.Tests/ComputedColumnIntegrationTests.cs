using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Server-computed column end-to-end against real MySQL: the database computes <c>FullName</c> from the
/// stored DDL expression; insert/update never write it, and reads materialize the computed value.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class ComputedColumnIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public ComputedColumnIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ComputedValueIsCalculatedByDatabaseOnInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ComputedColumnMySqlDdl, "computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var inserted = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace", BaseValue = 2, MixedCaseValue = 3 }))!;
        Assert.Equal("Ada Lovelace", inserted.FullName);
        Assert.Equal(5, inserted.ComputedTotal);

        var loaded = (await store.SelectByKeyAsync(inserted.Id))!;
        Assert.Equal("Ada Lovelace", loaded.FullName);
    }

    [SkippableFact]
    public async Task ComputedValueTracksUpdatesToSourceColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ComputedColumnMySqlDdl, "computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var doc = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper", BaseValue = 4, MixedCaseValue = 5 }))!;

        Assert.True(await store.UpdateAsync(new ComputedPerson { Id = doc.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored", BaseValue = 10, MixedCaseValue = 7 }));

        var after = (await store.SelectByKeyAsync(doc.Id))!;
        Assert.Equal("Grace Murray", after.FullName);
        Assert.Equal(17, after.ComputedTotal);
    }
}
