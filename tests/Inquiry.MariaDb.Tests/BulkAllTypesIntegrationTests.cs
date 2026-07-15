using Inquiry.FeatureCatalog;
using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through MariaDbBulkCopy on MariaDB.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesMySqlDdl, "bulkalltypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await BulkAllTypesCases.RunAsync(store);
    }
}
