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

        // One ;-separated command read through the grid reader. Assert the WHOLE line: child sets come
        // first and the parent set last (#70), and only the full text pins that order.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("var _sql = _sql_Territories_All + \";\" + _sqlSelectAll;", text);

        // Parents stream straight out of the reader — no intermediate list, no second pass.
        Assert.Contains("_grid.ReadStreamAsync<", text);
        Assert.DoesNotContain("_grid.ReadListAsync<", text);
        Assert.DoesNotContain("var _entities", text);
        // No per-relation streaming query for the child collection on the grid path. Match the text the
        // separate path actually emits: the child loop wraps its source in a fully-qualified static
        // ConfigureAwait, so a bare "Inquiry.QueryAsync<" literal here would never match anything.
        Assert.DoesNotContain("TaskAsyncEnumerableExtensions.ConfigureAwait(Inquiry.QueryAsync<", text);
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
        Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sql_Territories_All + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sqlSelectAll + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);

        // Parents stream straight out of the reader — no intermediate list, no second pass.
        Assert.Contains("_grid.ReadStreamAsync<", text);
        Assert.DoesNotContain("_grid.ReadListAsync<", text);
        Assert.DoesNotContain("var _entities", text);
        // No per-relation streaming query for the child collection on the grid path. Match the text the
        // separate path actually emits: the child loop wraps its source in a fully-qualified static
        // ConfigureAwait, so a bare "Inquiry.QueryAsync<" literal here would never match anything.
        Assert.DoesNotContain("TaskAsyncEnumerableExtensions.ConfigureAwait(Inquiry.QueryAsync<", text);
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

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Post> SelectAllWithRelationsAsync(CancellationToken ct = default);
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

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void SelectAllEager_MixedCollectionAndReference_UsesOneGridCommand(string dialect)
    {
        var result = RunGenerator(MixedRelationEagerSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var text = GetPostStoreText(result);

        // One grid command carrying the parent SELECT plus both relation SELECTs.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("_sql_Tags_All", text);
        Assert.Contains("_sql_Author_All", text);

        // The collection relation groups by the child FK; the reference relation indexes the
        // referenced rows by their own key. Both stream through ReadForEachAsync on the grid path.
        Assert.Contains("_grouped_Tags", text);
        Assert.Contains("_parents_Author", text);
        Assert.Contains("_grid.ReadForEachAsync<", text);

        // No per-relation round trip. This is the literal the separate path actually emits — the child
        // loop wraps its source in a fully-qualified static ConfigureAwait, so asserting on a bare
        // "Inquiry.QueryAsync<" would be unfalsifiable.
        Assert.DoesNotContain("TaskAsyncEnumerableExtensions.ConfigureAwait(Inquiry.QueryAsync<", text);
    }

    // A relation whose child type is not an [InquiryTable] entity is silently skipped — no diagnostic
    // (InquiryGeneratorBase.ValidateRelations) and no child fetch. It must not drag its mapped siblings
    // off the grid path with it (#70).
    private const string PartiallyMappedRelationEagerSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Tag")]
        public sealed class Tag
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn] public int PostId { get; set; }
        }

        // Deliberately NOT an [InquiryTable] entity.
        public sealed class Note
        {
            public int Id { get; set; }
            public int PostId { get; set; }
        }

        [InquiryTable("Post")]
        public sealed class Post
        {
            [InquiryKey] public int Id { get; set; }

            [InquiryRelation(nameof(Tag.PostId))]
            public IReadOnlyList<Tag> Tags { get; set; } = new List<Tag>();

            [InquiryRelation("PostId")]
            public IReadOnlyList<Note> Notes { get; set; } = new List<Note>();
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
    [InlineData("Oracle")]
    public void SelectOneByKeyEager_UnmappedRelation_StillGridsMappedRelations(string dialect)
    {
        var result = RunGenerator(PartiallyMappedRelationEagerSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var text = GetPostStoreText(result);

        // The mapped relation still resolves in one round trip; the unmapped one is simply absent.
        Assert.Contains("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("_sql_Tags", text);
        Assert.Contains("_grid.ReadListAsync<", text);
        Assert.DoesNotContain("_sql_Notes", text);
        Assert.DoesNotContain("_entity.Notes", text);
    }

    // An eager method on an entity with no emittable relation falls off the grid path onto the
    // separate-query path. That path streams with `await foreach`, and IAsyncEnumerable<T>.ConfigureAwait
    // is an EXTENSION method — generated stores emit no usings, so it has to be called as a static or the
    // generated file does not compile.
    private const string NoRelationEagerSource = """
        using System.Collections.Generic;
        using System.Threading;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Region")]
        public sealed class Region
        {
            [InquiryKey] public int RegionId { get; set; }
        }

        public partial class RegionStore : InquiryStore<Region>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Region> SelectAllEagerAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void SelectAllEager_WithoutRelations_EmitsCompilableSeparatePath(string dialect)
    {
        var result = RunGenerator(NoRelationEagerSource, dialect: dialect);
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(errors);

        var text = GetRegionStoreText(result);

        // No relations to batch, so no grid command.
        Assert.DoesNotContain("Inquiry.QueryMultipleAsync(", text);
        Assert.Contains("global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(", text);

        // Pins the literal the grid tests above assert the ABSENCE of, so those guards stay falsifiable.
        Assert.Contains("TaskAsyncEnumerableExtensions.ConfigureAwait(Inquiry.QueryAsync<", text);
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
