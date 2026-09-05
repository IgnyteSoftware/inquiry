using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("NegatedProduct")]
public sealed class NegatedProduct
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Qty")]
    public int Qty { get; set; }
}

public partial class NegatedProductStore : InquiryStore<NegatedProduct>
{
    [InquiryInsert]
    public partial Task<NegatedProduct?> InsertAsync(NegatedProduct product, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.NotLike)]
    public partial Task<IReadOnlyList<NegatedProduct>> NameNotLikeAsync(string pattern, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Qty", Compare.NotBetween)]
    public partial Task<IReadOnlyList<NegatedProduct>> QtyNotBetweenAsync(int low, int high, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere("Qty", Compare.NotIn)]
    public partial Task<IReadOnlyList<NegatedProduct>> QtyNotInAsync(IReadOnlyList<int> qtys, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class NegatedOperatorIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public NegatedOperatorIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE [NegatedProduct] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL, [Qty] INT NOT NULL);";

    [SkippableFact]
    public async Task NotLikeExcludesPatternMatches()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "negated");
        var store = harness.GetRequiredService<NegatedProductStore>();

        await store.InsertAsync(new NegatedProduct { Name = "Widget", Qty = 5 });
        await store.InsertAsync(new NegatedProduct { Name = "Gadget", Qty = 15 });
        await store.InsertAsync(new NegatedProduct { Name = "Gizmo Test", Qty = 25 });

        var result = await store.NameNotLikeAsync("%Test%");
        Assert.Equal(new[] { "Gadget", "Widget" }, result.Select(p => p.Name).OrderBy(n => n).ToArray());
    }

    [SkippableFact]
    public async Task NotBetweenExcludesInclusiveRange()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "negated");
        var store = harness.GetRequiredService<NegatedProductStore>();

        await store.InsertAsync(new NegatedProduct { Name = "Widget", Qty = 5 });
        await store.InsertAsync(new NegatedProduct { Name = "Gadget", Qty = 15 });
        await store.InsertAsync(new NegatedProduct { Name = "Gizmo Test", Qty = 25 });

        var result = await store.QtyNotBetweenAsync(10, 20);
        Assert.Equal(new[] { 5, 25 }, result.Select(p => p.Qty).OrderBy(q => q).ToArray());
    }

    [SkippableFact]
    public async Task NotInExcludesListedValues()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "negated");
        var store = harness.GetRequiredService<NegatedProductStore>();

        await store.InsertAsync(new NegatedProduct { Name = "Widget", Qty = 5 });
        await store.InsertAsync(new NegatedProduct { Name = "Gadget", Qty = 15 });
        await store.InsertAsync(new NegatedProduct { Name = "Gizmo Test", Qty = 25 });

        var result = await store.QtyNotInAsync(new[] { 15, 25 });
        var only = Assert.Single(result);
        Assert.Equal(5, only.Qty);
    }

    [SkippableFact]
    public async Task EmptyNotInMatchesEveryRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "negated");
        var store = harness.GetRequiredService<NegatedProductStore>();

        await store.InsertAsync(new NegatedProduct { Name = "Widget", Qty = 5 });
        await store.InsertAsync(new NegatedProduct { Name = "Gadget", Qty = 15 });
        await store.InsertAsync(new NegatedProduct { Name = "Gizmo Test", Qty = 25 });

        var result = await store.QtyNotInAsync(Array.Empty<int>());
        Assert.Equal(3, result.Count);
    }
}
