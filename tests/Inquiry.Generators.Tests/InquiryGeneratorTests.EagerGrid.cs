using System;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #70: <c>[InquirySelectAllEager]</c> issues one multi-result-set command (parent SELECT + each relation
/// SELECT, read through an InquiryGridReader) instead of one round trip per relation — a <c>;</c>-separated
/// batch on SQLite/SqlServer/PostgreSql/MySql, a <c>DBMS_SQL.RETURN_RESULT</c> PL/SQL block on Oracle.
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
    [InlineData("MariaDb")]
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
    public void SelectAllEager_UsesOnePlSqlGridCommand_OnOracle()
    {
        var result = RunGenerator(SelectAllEagerSource, dialect: "Oracle");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var text = GetRegionStoreText(result);

        // Oracle multiplexes result sets through a DBMS_SQL.RETURN_RESULT PL/SQL block (implicit result
        // sets), read through the same grid reader as the ;-batching dialects.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("_grid.ReadListAsync<", text);
        Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sqlSelectAll + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Territories_All + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
        // No per-relation streaming query for the child collection on the grid path.
        Assert.DoesNotContain("await foreach (var _c in Inquiry.QueryAsync<", text);
    }

    private const string MixedRelationEagerSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Author")]
        public sealed class Author
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }

        [InquiryTable("Tag")]
        public sealed class Tag
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int PostId { get; set; }
            [InquiryColumn] public string Label { get; set; } = string.Empty;
        }

        [InquiryTable("Post")]
        public sealed class Post
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int AuthorId { get; set; }

            [InquiryRelation(nameof(AuthorId))]
            public Author? Author { get; set; }

            [InquiryRelation(nameof(Tag.PostId))]
            public IReadOnlyList<Tag> Tags { get; set; } = new List<Tag>();
        }

        public partial class PostStore : InquiryStore<Post>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Post?> GetWithRelationsAsync(int id, CancellationToken ct = default);
        }
        """;

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void SelectOneByKeyEager_MixedCollectionAndReference_UsesOneGridCommand(string dialect)
    {
        var result = RunGenerator(MixedRelationEagerSource, dialect: dialect);
        Assert.Empty(result.GeneratorDiagnostics);

        var text = GetPostStoreText(result);

        // One grid command containing all three result sets.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);

        // Reference relation uses the _ByKey subquery const and ReadGeneratedSingleOrDefaultAsync.
        Assert.Contains("_sql_Author_ByKey", text);
        Assert.Contains("_grid.ReadGeneratedSingleOrDefaultAsync<", text);

        // Collection relation uses the standard const and ReadListAsync.
        Assert.Contains("_sql_Tags", text);
        Assert.Contains("_grid.ReadListAsync<", text);

        // Two parameters: parent key + collection FK (reference relation reuses parent key via subquery).
        Assert.Contains("_p0.ParameterName", text);
        Assert.Contains("_p1.ParameterName", text);
        Assert.DoesNotContain("_p2", text);
    }

    [Fact]
    public void SelectOneByKeyEager_MixedCollectionAndReference_UsesOneGridCommand_Oracle()
    {
        var result = RunGenerator(MixedRelationEagerSource, dialect: "Oracle");
        Assert.Empty(result.GeneratorDiagnostics);

        var text = GetPostStoreText(result);

        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("_sql_Author_ByKey", text);
        Assert.Contains("_grid.ReadGeneratedSingleOrDefaultAsync<", text);
        Assert.Contains("_grid.ReadListAsync<", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT", text);
    }

    private static string GetRegionStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("RegionStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }

    private static string GetPostStoreText(GeneratorTestResult result)
    {
        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("PostStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return generatedStore.GetText().ToString();
    }
}
