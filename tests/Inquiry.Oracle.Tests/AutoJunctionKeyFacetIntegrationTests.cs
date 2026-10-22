using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Auto-managed junctions over non-integer keys with overridden columns (#280, #281), created from the
/// generated DDL. The scenario is shared through <see cref="AutoJunctionKeyFacetAssertions"/>.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class AutoJunctionKeyFacetIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public AutoJunctionKeyFacetIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    // The generated Oracle DDL leaves these identifiers unquoted, so they fold to upper case.
    private static string Quote(string identifier) => identifier;

    private Task<OracleTestHarness> HarnessAsync(string name)
        => OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, AutoJunctionKeyFacetAssertions.Tables),
            name);

    [SkippableFact]
    public async Task AutoJunctionOverNonIntegerKeysKeepsKeyTypesAndForeignKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajfacets");
        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
            connection,
            Quote,
            static (table, column) =>
                "SELECT DATA_TYPE, CHAR_LENGTH, DATA_PRECISION, DATA_SCALE FROM USER_TAB_COLUMNS "
                + $"WHERE TABLE_NAME = UPPER('{table}') AND COLUMN_NAME = UPPER('{column}')",
            new AjKeyTypes(Name: "NVARCHAR2/24//", Amount: "NUMBER/0/9/2", Sku: "VARCHAR2/12//"));
    }

    [SkippableFact]
    public async Task AutoJunctionWithOverriddenColumnsReadsBothNavigations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajreads");
        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.BothNavigationsReadTheirOwnLinksAsync(
            harness.GetRequiredService<AjCodeStore>(),
            harness.GetRequiredService<AjPriceStore>(),
            harness.GetRequiredService<AjSkuStore>(),
            connection,
            Quote);
    }
}
