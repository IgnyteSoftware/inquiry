using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

[InquiryTable("DistinctProduct")]
public sealed class DistinctProduct
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Category")]
    public string Category { get; set; } = string.Empty;
}

[InquiryProjection(typeof(DistinctProduct))]
public sealed record DistinctCategory
{
    [InquiryColumn("Category")]
    public string Category { get; init; } = string.Empty;
}

public partial class DistinctProductStore : InquiryStore<DistinctProduct>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(DistinctProduct product, CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true)]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectDistinctAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Category", Distinct = true)]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectDistinctByCategoryAsync(string category, CancellationToken cancellationToken = default);

    [InquirySelectAll(Distinct = true)]
    public partial Task<IReadOnlyList<DistinctCategory>> DistinctCategoriesAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<DistinctProduct>> SelectAllAsync(CancellationToken cancellationToken = default);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class DistinctIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public DistinctIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """CREATE TABLE "DistinctProduct" ("Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL, "Category" TEXT NOT NULL);""";

    [SkippableFact]
    public async Task DistinctSelectAllDeduplicatesRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "B" });

        var distinct = await store.SelectDistinctAsync();
        var all = await store.SelectAllAsync();

        Assert.Equal(all.Count, distinct.Count);
    }

    [SkippableFact]
    public async Task DistinctSelectAllByFieldFiltersAndDeduplicates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Doohickey", Category = "B" });

        var result = await store.SelectDistinctByCategoryAsync("A");
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal("A", p.Category));
    }

    [SkippableFact]
    public async Task DistinctProjectionReturnsUniqueCategories()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "distinct");
        var store = harness.GetRequiredService<DistinctProductStore>();

        await store.InsertAsync(new DistinctProduct { Name = "Widget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Gadget", Category = "A" });
        await store.InsertAsync(new DistinctProduct { Name = "Doohickey", Category = "B" });

        var categories = await store.DistinctCategoriesAsync();

        Assert.Equal(2, categories.Count);
        Assert.Contains(categories, c => c.Category == "A");
        Assert.Contains(categories, c => c.Category == "B");
    }
}
