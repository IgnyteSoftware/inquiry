using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through Oracle's batch-INSERT fallback.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesOracleDdl, "bulkalltypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await BulkAllTypesCases.RunAsync(store);
    }
}
