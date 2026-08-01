using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through MySqlBulkCopy on MySQL.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesMySqlDdl, "bulkalltypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await BulkAllTypesCases.RunAsync(store);
    }
}
