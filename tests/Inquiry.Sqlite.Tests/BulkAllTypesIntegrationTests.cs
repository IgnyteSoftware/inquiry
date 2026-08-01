using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through SQLite's batch-INSERT fallback.
/// </summary>
public sealed class BulkAllTypesIntegrationTests
{
    [Fact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.BulkAllTypesSqliteDdl, "BulkAllTypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await BulkAllTypesCases.RunAsync(store);
    }
}
