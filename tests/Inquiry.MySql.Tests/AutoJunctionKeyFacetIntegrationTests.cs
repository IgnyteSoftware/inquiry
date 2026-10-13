using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.MySql.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Auto-managed junctions over non-integer keys with overridden columns (#280, #281), created from the
/// generated DDL. The scenario is shared through <see cref="AutoJunctionKeyFacetAssertions"/>.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class AutoJunctionKeyFacetIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public AutoJunctionKeyFacetIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private static string Quote(string identifier) => "`" + identifier + "`";

    private Task<MySqlTestHarness> HarnessAsync(string name)
        => MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, AutoJunctionKeyFacetAssertions.Tables),
            name);

    [SkippableFact]
    public async Task AutoJunctionOverNonIntegerKeysKeepsKeyTypesAndForeignKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajfacets");
        await using var connection = new MySqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
            connection,
            Quote,
            static (table, column) =>
                "SELECT COLUMN_TYPE FROM information_schema.columns "
                + $"WHERE table_schema = DATABASE() AND table_name = '{table}' AND column_name = '{column}'",
            new AjKeyTypes(Name: "varchar(24)", Amount: "decimal(9,2)", Sku: "varchar(12)"));
    }

    [SkippableFact]
    public async Task AutoJunctionWithOverriddenColumnsReadsBothNavigations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajreads");
        await using var connection = new MySqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.BothNavigationsReadTheirOwnLinksAsync(
            harness.GetRequiredService<AjCodeStore>(),
            harness.GetRequiredService<AjPriceStore>(),
            harness.GetRequiredService<AjSkuStore>(),
            connection,
            Quote);
    }
}
