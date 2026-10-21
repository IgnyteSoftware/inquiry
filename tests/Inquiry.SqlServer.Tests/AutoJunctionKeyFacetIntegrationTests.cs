using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Auto-managed junctions over non-integer keys with overridden columns (#280, #281), created from the
/// generated DDL. The scenario is shared through <see cref="AutoJunctionKeyFacetAssertions"/>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AutoJunctionKeyFacetIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public AutoJunctionKeyFacetIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private static string Quote(string identifier) => "[" + identifier + "]";

    private Task<SqlServerTestHarness> HarnessAsync(string name)
        => SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, AutoJunctionKeyFacetAssertions.Tables),
            name,
            false);

    [SkippableFact]
    public async Task AutoJunctionOverNonIntegerKeysKeepsKeyTypesAndForeignKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajfacets");
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
            connection,
            Quote,
            static (table, column) =>
                "SELECT DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS "
                + $"WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'",
            new AjKeyTypes(Name: "nvarchar/24//", Amount: "decimal//9/2", Sku: "varchar/12//"));
    }

    [SkippableFact]
    public async Task AutoJunctionWithOverriddenColumnsReadsBothNavigations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajreads");
        await using var connection = new SqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.BothNavigationsReadTheirOwnLinksAsync(
            harness.GetRequiredService<AjCodeStore>(),
            harness.GetRequiredService<AjPriceStore>(),
            harness.GetRequiredService<AjSkuStore>(),
            connection,
            Quote);
    }
}
