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

        var person = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace" }))!;

        Assert.Equal("Ada Lovelace", person.FullName);

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

        var person = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper" }))!;

        await store.UpdateAsync(new ComputedPerson { Id = person.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored" });
        var reloaded = (await store.SelectByKeyAsync(person.Id))!;

        Assert.Equal("Grace Murray", reloaded.FullName);
    }
}
