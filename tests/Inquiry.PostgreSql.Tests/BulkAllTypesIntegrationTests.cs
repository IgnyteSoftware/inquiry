using Inquiry.FeatureCatalog;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through Npgsql binary COPY on PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesPostgreSqlDdl, "bulkalltypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await BulkAllTypesCases.RunAsync(store);
    }
}
