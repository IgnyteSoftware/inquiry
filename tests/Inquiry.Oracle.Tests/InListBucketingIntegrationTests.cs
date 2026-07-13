using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Oracle.Tests;

[InquiryTable("OracleInItem")]
public sealed class OracleInItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int? CategoryId { get; set; }
}

public partial class OracleInItemStore : InquiryStore<OracleInItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(OracleInItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("CategoryId", Compare.In)]
    public partial Task<IReadOnlyList<OracleInItem>> InCategoriesAsync(
        IReadOnlyList<int> categoryId,
        CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("CategoryId", Compare.NotIn)]
    public partial Task<IReadOnlyList<OracleInItem>> NotInCategoriesAsync(
        IReadOnlyList<int> categoryId,
        CancellationToken cancellationToken = default);
}

/// <summary>Live coverage of Oracle's positive-IN JSON_TABLE path and scalar NOT IN expansion.</summary>
[Collection(OracleCollection.Name)]
public sealed class InListBucketingIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public InListBucketingIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE OracleInItem (Id NUMBER(10) PRIMARY KEY, CategoryId NUMBER(10) NULL)";

    private async Task<OracleTestHarness> SeedAsync()
    {
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "in_list");
        var items = harness.GetRequiredService<OracleInItemStore>();

        await items.InsertAsync(new OracleInItem { Id = 1, CategoryId = 1 });
        await items.InsertAsync(new OracleInItem { Id = 2, CategoryId = 1 });
        await items.InsertAsync(new OracleInItem { Id = 3, CategoryId = 2 });
        await items.InsertAsync(new OracleInItem { Id = 4, CategoryId = 2 });
        await items.InsertAsync(new OracleInItem { Id = 5, CategoryId = null });
        return harness;
    }

    [SkippableFact]
    public async Task InListUsesJsonTableAndReturnsCorrectRowsAcrossCardinalities()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var items = harness.GetRequiredService<OracleInItemStore>();

        // Oracle positive IN binds one JSON array and expands it with JSON_TABLE, so every
        // cardinality exercises the same bounded-parameter SQL shape.
        foreach (var count in new[] { 1, 2, 3, 5, 9 })
        {
            var categories = new List<int> { 1 };
            for (var i = 1; i < count; i++)
            {
                categories.Add(1000 + i);
            }

            var matched = await items.InCategoriesAsync(categories);
            Assert.Equal(2, matched.Count);
            Assert.All(matched, item => Assert.Equal(1, item.CategoryId));
        }

        var duplicates = await items.InCategoriesAsync(new[] { 1, 1, 1 });
        Assert.Equal(2, duplicates.Count);
    }

    [SkippableFact]
    public async Task NotInUsesScalarExpansionWithoutChangingSemantics()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var items = harness.GetRequiredService<OracleInItemStore>();

        // NOT IN deliberately uses scalar parameters. Repeated values used to stabilize SQL shapes
        // must not change the excluded set; NULL remains UNKNOWN under normal SQL semantics.
        var one = await items.NotInCategoriesAsync(new[] { 2 });
        var repeated = await items.NotInCategoriesAsync(new[] { 2, 2, 2 });

        Assert.Equal(new[] { 1, 2 }, one.Select(item => item.Id).OrderBy(id => id));
        Assert.Equal(new[] { 1, 2 }, repeated.Select(item => item.Id).OrderBy(id => id));

        var all = await items.NotInCategoriesAsync(Array.Empty<int>());
        Assert.Equal(5, all.Count);
    }
}
