using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Bulk insert over the shared <see cref="BulkItem"/> catalog entity against real SQL Server:
/// <c>[InquiryBulkInsert]</c> streams rows through <c>SqlBulkCopy</c>, returns the rows-written
/// count, round-trips decimals and null/non-null strings, and an empty enumerable is a no-op.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BulkInsertIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public BulkInsertIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertStreamsAllRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkins");
        var store = harness.GetRequiredService<BulkItemStore>();

        var rows = Enumerable.Range(0, 500).Select(i => new BulkItem
        {
            Category = i % 2 == 0 ? "alpha" : "beta",
            Amount = i + 0.25m,
            Note = i % 3 == 0 ? null : $"note-{i}",
        }).ToArray();

        var written = await store.BulkInsertAsync(rows);

        Assert.Equal(500, written);
        Assert.Equal(500, await store.CountAsync());

        var alphas = await store.ByCategoryAsync("alpha");
        Assert.Equal(250, alphas.Count);
        var betas = await store.ByCategoryAsync("beta");
        Assert.Equal(250, betas.Count);

        var withoutNote = alphas.Single(item => item.Amount == 0.25m); // i = 0: divisible by 3 → null Note
        Assert.Null(withoutNote.Note);

        var withNote = alphas.Single(item => item.Amount == 4.25m); // i = 4
        Assert.Equal("note-4", withNote.Note);
    }

    [SkippableFact]
    public async Task EmptyEnumerableReturnsZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.SqlServerDdl, "bulkempty");
        var store = harness.GetRequiredService<BulkItemStore>();

        Assert.Equal(0, await store.BulkInsertAsync(Array.Empty<BulkItem>()));
        Assert.Equal(0, await store.CountAsync());
    }
}
