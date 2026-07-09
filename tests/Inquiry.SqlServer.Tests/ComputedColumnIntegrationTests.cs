using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Server-computed column end-to-end against real SQL Server: the database computes <c>FullName</c> from the
/// stored DDL expression; insert/update never write it, and reads materialize the computed value.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ComputedColumnIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ComputedColumnIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ComputedValueIsCalculatedByDatabaseOnInsert()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ComputedColumnSqlServerDdl, "computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var inserted = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace" }))!;
        Assert.Equal("Ada Lovelace", inserted.FullName);

        var loaded = (await store.SelectByKeyAsync(inserted.Id))!;
        Assert.Equal("Ada Lovelace", loaded.FullName);
    }

    [SkippableFact]
    public async Task ComputedValueTracksUpdatesToSourceColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.ComputedColumnSqlServerDdl, "computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var doc = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper" }))!;

        Assert.True(await store.UpdateAsync(new ComputedPerson { Id = doc.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored" }));

        var after = (await store.SelectByKeyAsync(doc.Id))!;
        Assert.Equal("Grace Murray", after.FullName);
    }
}
