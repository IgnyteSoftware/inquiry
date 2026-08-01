using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class ComputedColumnIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public ComputedColumnIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ComputedValueIsCalculatedByDatabaseOnInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ComputedColumnOracleDdl, "computed");

        var store = harness.GetRequiredService<ComputedPersonStore>();

        var person = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace", BaseValue = 2, MixedCaseValue = 3 }))!;

        Assert.Equal("Ada Lovelace", person.FullName);
        Assert.Equal(5, person.ComputedTotal);

        var reloaded = (await store.SelectByKeyAsync(person.Id))!;
        Assert.Equal("Ada Lovelace", reloaded.FullName);
    }

    [SkippableFact]
    public async Task ComputedValueTracksUpdatesToSourceColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ComputedColumnOracleDdl, "computed");

        var store = harness.GetRequiredService<ComputedPersonStore>();

        var person = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper", BaseValue = 4, MixedCaseValue = 5 }))!;

        await store.UpdateAsync(new ComputedPerson { Id = person.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored", BaseValue = 10, MixedCaseValue = 7 });
        var reloaded = (await store.SelectByKeyAsync(person.Id))!;

        Assert.Equal("Grace Murray", reloaded.FullName);
        Assert.Equal(17, reloaded.ComputedTotal);
    }
}
