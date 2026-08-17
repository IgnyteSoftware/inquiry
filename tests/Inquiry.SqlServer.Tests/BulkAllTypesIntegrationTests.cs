using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Exact boundary all-types bulk-insert matrix (#134) through SqlBulkCopy on SQL Server.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ExactBoundaryAllTypesRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesSqlServerDdl, "bulkalltypes");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();
        await using var transaction = await inquiry.BeginTransactionAsync();
        await BulkAllTypesCases.RunAsync(store);
        await transaction.CommitAsync();
    }
}
