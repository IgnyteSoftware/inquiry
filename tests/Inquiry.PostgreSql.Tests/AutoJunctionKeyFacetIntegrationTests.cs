using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Generated;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Auto-managed junctions over non-integer keys with overridden columns (#280, #281), created from the
/// generated DDL. The scenario is shared through <see cref="AutoJunctionKeyFacetAssertions"/>.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AutoJunctionKeyFacetIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public AutoJunctionKeyFacetIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private static string Quote(string identifier) => "\"" + identifier + "\"";

    private Task<PostgreSqlTestHarness> HarnessAsync(string name)
        => PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            GeneratedSchemaDdl.Extract(InquiryGeneratedSchema.Ddl, AutoJunctionKeyFacetAssertions.Tables),
            name);

    [SkippableFact]
    public async Task AutoJunctionOverNonIntegerKeysKeepsKeyTypesAndForeignKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajfacets");
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
            connection,
            Quote,
            static (table, column) =>
                $"SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a WHERE a.attrelid = '\"{table}\"'::regclass AND a.attname = '{column}'",
            new AjKeyTypes(Name: "character varying(24)", Amount: "numeric(9,2)", Sku: "character varying(12)"));
    }

    [SkippableFact]
    public async Task AutoJunctionWithOverriddenColumnsReadsBothNavigations()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await HarnessAsync("ajreads");
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await AutoJunctionKeyFacetAssertions.BothNavigationsReadTheirOwnLinksAsync(
            harness.GetRequiredService<AjCodeStore>(),
            harness.GetRequiredService<AjPriceStore>(),
            harness.GetRequiredService<AjSkuStore>(),
            connection,
            Quote);
    }
}
