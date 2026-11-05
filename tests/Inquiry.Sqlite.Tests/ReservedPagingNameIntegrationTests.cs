using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Properties and a JSON-path leaf named like the offset-paging bind names (#543). The filter value
/// and the offset and limit must bind to separate parameters, so each call returns the requested page.
/// </summary>
public sealed class ReservedPagingNameIntegrationTests
{
    private static async Task<SqliteTestHarness> CreateSeededHarnessAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ReservedPagingNameDocSqliteDdl, "ReservedPagingName");
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

    [Fact]
    public async Task FieldReadOnOffsetPropertyReturnsTheRequestedPage()
    {
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.ByOffsetFieldAsync(3, offset: 1, limit: 2)));
    }

    [Fact]
    public async Task FieldReadOnLimitPropertyReturnsTheRequestedPage()
    {
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.ByLimitFieldAsync(5, offset: 1, limit: 2)));
    }

    [Fact]
    public async Task CriterionOnOffsetPropertyReturnsTheRequestedPage()
    {
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereOffsetAsync(3, offset: 1, limit: 2)));
    }

    [Fact]
    public async Task CriterionOnLimitPropertyReturnsTheRequestedPage()
    {
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereLimitAsync(5, offset: 1, limit: 2)));
    }

    [Fact]
    public async Task JsonPathCriterionOnLimitLeafReturnsTheRequestedPage()
    {
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedPagingNameDocStore>();

        Assert.Equal(["r2", "r3"], Titles(await store.WhereJsonLimitAsync("x", offset: 1, limit: 2)));
    }

    [Fact]
    public async Task JsonPathCriterionOnAnOrdinaryEntityReturnsTheRequestedPage()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.ReservedPagingJsonDocSqliteDdl, "ReservedPagingJson");
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
