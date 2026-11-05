using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Properties and a JSON-path leaf named like the offset-paging bind names (#543). The filter value
/// and the offset and limit must bind to separate parameters, so each call returns the requested page.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class ReservedPagingNameIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public ReservedPagingNameIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private async Task<MySqlTestHarness> CreateSeededHarnessAsync()
    {
        var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ReservedPagingNameDocMySqlDdl, "rpn");
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();
        // Rows 1-4 match every filter below; rows 5-6 hold the values a swapped bind would use.
        for (var i = 1; i <= 6; i++)
        {
            var match = i <= 4;
            await store.InsertAsync(new ReservedPagingNameDoc
            {
                __offset = match ? 3 : 1,
                __Limit = match ? 5 : 2,
                Data = match ? """{"__limit":"x"}""" : """{"__limit":"2"}""",
                Title = "r" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        return harness;
    }

    private static string[] Titles(IReadOnlyList<ReservedPagingNameDoc> rows)
        => rows.Select(static row => row.Title).ToArray();

    [SkippableFact]
    public async Task FieldReadOnOffsetPropertyReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.ByOffsetFieldAsync(3, offset: 1, limit: 2)));
    }

    [SkippableFact]
    public async Task FieldReadOnLimitPropertyReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.ByLimitFieldAsync(5, offset: 1, limit: 2)));
    }

    [SkippableFact]
    public async Task CriterionOnOffsetPropertyReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereOffsetAsync(3, offset: 1, limit: 2)));
    }

    [SkippableFact]
    public async Task CriterionOnLimitPropertyReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereLimitAsync(5, offset: 1, limit: 2)));
    }

    [SkippableFact]
    public async Task JsonPathCriterionOnLimitLeafReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereJsonLimitAsync("x", offset: 1, limit: 2)));
    }

    [SkippableFact]
    public async Task JsonPathCriterionOnAnOrdinaryEntityReturnsTheRequestedPage()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ReservedPagingJsonDocMySqlDdl, "rpj");
        var store = harness.GetRequiredService<ReservedPagingJsonDocStore>();
        for (var i = 1; i <= 6; i++)
        {
            await store.InsertAsync(new ReservedPagingJsonDoc
            {
                Data = i <= 4 ? """{"__limit":"x"}""" : """{"__limit":"2"}""",
                Title = "r" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        var rows = await store.WhereJsonLimitAsync("x", offset: 1, limit: 2);
        Assert.Equal(["r2", "r3"], rows.Select(static row => row.Title).ToArray());
    }
}
