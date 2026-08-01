using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> against real MySQL via the shared <see cref="BulkItem"/> catalog entity: the
/// generated store method streams rows through MySqlConnector's <c>MySqlBulkCopy</c> (LOAD DATA LOCAL
/// INFILE) using the provider-registered <c>IInquiryBulkCopier</c>, mapping columns by name so the
/// omitted AUTO_INCREMENT key stays server-generated.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public BulkInsertIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsAllRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulk");
        var store = harness.GetRequiredService<BulkItemStore>();

        var items = Enumerable.Range(1, 500).Select(i => new BulkItem
        {
            Category = i % 2 == 0 ? "even" : "odd",
            Amount = i + 0.25m,
            Note = i % 3 == 0 ? null : $"note {i}",
        }).ToList();

        var inserted = await store.BulkInsertAsync(items);

        Assert.Equal(500, inserted);
        Assert.Equal(500, await store.CountAsync());

        var even = await store.ByCategoryAsync("even");
        Assert.Equal(250, even.Count);
        var sample = Assert.Single(even, x => x.Amount == 2.25m);
        Assert.Equal("note 2", sample.Note);
        Assert.Contains(even, x => x.Note is null);
    }

    [SkippableFact]
    public async Task BulkInsertOfEmptySequenceReturnsZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.MySqlDdl, "bulk");
        var store = harness.GetRequiredService<BulkItemStore>();

        var inserted = await store.BulkInsertAsync(Enumerable.Empty<BulkItem>());

        Assert.Equal(0, inserted);
        Assert.Equal(0, await store.CountAsync());
    }
}
