using System.Collections.Generic;
using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c> against real PostgreSQL via the shared <see cref="BulkItem"/> catalog
/// entity: the store method streams rows through Npgsql binary COPY
/// (<see cref="PostgreSqlBulkCopier"/>), returns the written count, and round-trips values
/// including null text and decimals; an empty enumerable writes nothing.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public BulkInsertIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsRowsThroughBinaryCopyAndRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkins");
        var store = harness.GetRequiredService<BulkItemStore>();

        var items = Enumerable.Range(0, 500)
            .Select(i => new BulkItem
            {
                Category = i % 2 == 0 ? "even" : "odd",
                Amount = 1.25m * i,
                Note = i % 5 == 0 ? null : "note-" + i,
            })
            .ToList();

        var written = await store.BulkInsertAsync(items);

        Assert.Equal(500L, written);
        Assert.Equal(500L, await store.CountAsync());

        var even = await store.ByCategoryAsync("even");
        Assert.Equal(250, even.Count);
        Assert.Contains(even, i => i.Note is null && i.Amount == 0m); // i = 0: null Note round-trips
        Assert.Contains(even, i => i.Note == "note-2" && i.Amount == 2.50m); // decimal round-trips

        var odd = await store.ByCategoryAsync("odd");
        Assert.Equal(250, odd.Count);
        Assert.Contains(odd, i => i.Note == "note-499" && i.Amount == 1.25m * 499);
    }

    [SkippableFact]
    public async Task EmptyBulkInsertWritesNothing()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.PostgreSqlDdl, "bulkempty");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0L, await store.BulkInsertAsync(new List<BulkItem>()));
        Assert.Equal(0L, await store.CountAsync());
    }
}
