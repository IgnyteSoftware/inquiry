using System;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #70: <c>[InquirySelectAllEager]</c> issues one multi-result-set command (parent SELECT + each relation
/// SELECT, read through an InquiryGridReader) instead of one round trip per relation — on every dialect that
/// can return multiple result sets from one command. Oracle (ORA-00933) keeps the per-relation fallback.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string SelectAllEagerSource = """
        using System.Collections.Generic;
        using System.Threading;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Region")]
        public sealed class Region
        {
            [InquiryKey] public int RegionId { get; set; }

            [InquiryRelation(nameof(Territory.RegionId))]
            public IReadOnlyList<Territory> Territories { get; set; } = new List<Territory>();
        }

        [InquiryTable("Territory")]
        public sealed class Territory
        {
            [InquiryKey] public int TerritoryId { get; set; }
            [InquiryColumn] public int RegionId { get; set; }
        }

        public partial class RegionStore : InquiryStore<Region>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Region> SelectAllWithTerritoriesAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void SelectAllEager_UsesOneGridCommand_OnMultiResultDialects(string dialect)
    {
        var result = RunGenerator(SelectAllEagerSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var text = GetRegionStoreText(result);

        // One ;-separated command (parent + child) read through the grid reader.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("_grid.ReadListAsync<", text);
        Assert.Contains("\";\" + _sql_Territories_All", text);
        // No per-relation streaming query for the child collection on the grid path.
        Assert.DoesNotContain("await foreach (var _c in Inquiry.QueryAsync<", text);
    }

    [Fact]
    public void SelectAllEager_UsesSeparateRoundTrips_OnOracle()
    {
        var result = RunGenerator(SelectAllEagerSource, dialect: "Oracle");
        Assert.Empty(result.GeneratorDiagnostics);

        var text = GetRegionStoreText(result);

        // Oracle cannot multiplex result sets, so it keeps the per-relation query path.
        Assert.DoesNotContain("QueryMultipleAsync", text);
        Assert.DoesNotContain("_grid.ReadListAsync", text);
        Assert.Contains("await foreach (var _c in Inquiry.QueryAsync<", text);
        // (The separate-path `await foreach (…).ConfigureAwait(false)` resolves the
        // IAsyncEnumerable.ConfigureAwait extension via the consumer's global usings — present in real
        // projects (ImplicitUsings) and verified by the Oracle integration tests + the full solution build,
        // but not by this bare generator compilation. The grid dialects above compile clean here.)
    }

    private static string GetRegionStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("RegionStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }
}
