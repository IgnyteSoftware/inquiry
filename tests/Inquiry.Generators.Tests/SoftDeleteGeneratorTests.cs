using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Soft-delete emission tests: every SELECT AND-composes the active-row filter (via the
/// <c>AppendWhere</c> primitive), <c>[InquiryDelete]</c> becomes a soft UPDATE,
/// <c>HardDelete = true</c> keeps a literal DELETE, <c>IncludeDeleted = true</c> opts out, and
/// <c>[InquiryRestoreOneByKey]</c> clears the indicator. Also verifies the filter composes with the
/// predicate path and the paged path, the per-dialect literals (PG TRUE/FALSE, SqlServer
/// GETUTCDATE), the timestamp form, and the duplicate-column diagnostic.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string FlagEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TWidget")]
        public sealed class Widget
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("IsDeleted"), InquirySoftDelete]
            public bool IsDeleted { get; set; }
        }
        """;

    private static string WidgetStore(string methods) =>
        FlagEntity + "\n\npublic partial class WidgetStore : Inquiry.Stores.InquiryStore<Demo.Widget>\n{\n" + methods + "\n}\n";

    private static string GetWidgetStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private const string CrudMethods = """
        [InquirySelectAll]
        public partial Task<IReadOnlyList<Widget>> SelectAllAsync(CancellationToken cancellationToken = default);

        [InquirySelectOneByKey]
        public partial Task<Widget?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);

        [InquirySelectAllByField("Name")]
        public partial Task<IReadOnlyList<Widget>> SelectByNameAsync(string name, CancellationToken cancellationToken = default);

        [InquiryDelete]
        public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

        [InquiryRestoreOneByKey]
        public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);
        """;

    [Fact]
    public void SoftDeleteFiltersSelectsAndConvertsDeleteToUpdate_Sqlite()
    {
        var result = RunGenerator(WidgetStore(CrudMethods));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        // Every SELECT gains the active-row filter.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"IsDeleted\\\" = 0\";", text);
        Assert.Contains("_sqlSelectByKey = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"Id\\\" = @Id AND \\\"IsDeleted\\\" = 0\";", text);
        Assert.Contains("_sqlSelectBy_Name = \"SELECT \\\"Id\\\", \\\"Name\\\", \\\"IsDeleted\\\" FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name AND \\\"IsDeleted\\\" = 0\";", text);

        // Delete becomes a soft UPDATE; restore clears the flag.
        Assert.Contains("_sqlDeleteByKey = \"UPDATE \\\"TWidget\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("_sqlRestoreByKey = \"UPDATE \\\"TWidget\\\" SET \\\"IsDeleted\\\" = 0 WHERE \\\"Id\\\" = @Id\";", text);
        Assert.DoesNotContain("DELETE FROM", text);
    }

    [Fact]
    public void HardDeleteKeepsLiteralDeleteAlongsideSoftDefault_Sqlite()
    {
        var result = RunGenerator(WidgetStore("""
            [InquiryDelete(HardDelete = true)]
            public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("_sqlHardDeleteByKey = \"DELETE FROM \\\"TWidget\\\" WHERE \\\"Id\\\" = @Id\";", text);
        Assert.DoesNotContain("SET \\\"IsDeleted\\\"", text);
    }

    [Fact]
    public void IncludeDeletedEmitsUnfilteredSelect_Sqlite()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectAll(IncludeDeleted = true)]
            public partial Task<IReadOnlyList<Widget>> SelectAllIncludingDeletedAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        // The unfiltered per-method const has no WHERE clause.
        Assert.Contains("FROM \\\"TWidget\\\"\";", text);
        Assert.DoesNotContain("WHERE \\\"IsDeleted\\\" = 0", text);
    }

    [Fact]
    public void SoftDeleteFilterComposesWithPredicate_Sqlite()
    {
        // Composition: a predicate select also AND-composes the soft-delete filter.
        var result = RunGenerator(WidgetStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.Like)]
            public partial Task<IReadOnlyList<Widget>> SearchAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("\\\"Name\\\" LIKE @Name AND \\\"IsDeleted\\\" = 0", text);
    }

    [Fact]
    public void SoftDeleteFilterParenthesizesOrPredicatesBeforeComposing_Sqlite()
    {
        var result = RunGenerator(WidgetStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name")]
            [InquiryWhere("IsDeleted", Or = true)]
            public partial Task<IReadOnlyList<Widget>> SearchAsync(string name, bool deleted, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains(
            "WHERE (\\\"Name\\\" = @Name OR \\\"IsDeleted\\\" = @IsDeleted) AND \\\"IsDeleted\\\" = 0",
            text);
    }

    [Fact]
    public void SoftDeleteFilterComposesWithPaging_Sqlite()
    {
        // Composition: an offset-paged select keeps the filter before ORDER BY / LIMIT.
        var result = RunGenerator(WidgetStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true)]
            public partial Task<IReadOnlyList<Widget>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("WHERE \\\"IsDeleted\\\" = 0 ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @__offset", text);
    }

    [Fact]
    public void SoftDeleteFilterComposesWithKeyset_Sqlite()
    {
        // Keyset composition: the soft-delete filter is AND-appended after the cursor predicate
        // (this path composes inline in StoreProcessor, not via the SqlBuilder AppendWhere helper).
        var result = RunGenerator(WidgetStore("""
            [InquiryKeysetPage("Id")]
            public partial Task<Inquiry.Paging.InquiryPage<Widget, long>> PageAsync(long? afterId, int pageSize, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        // Seek query: the bare sargable cursor predicate AND-composed with the soft-delete filter.
        Assert.Contains("WHERE \\\"Id\\\" > @__cursor0 AND \\\"IsDeleted\\\" = 0 ORDER BY", text);
        // First-page query (null cursor): the soft-delete filter only, no cursor predicate.
        Assert.Contains("FROM \\\"TWidget\\\" WHERE \\\"IsDeleted\\\" = 0 ORDER BY \\\"Id\\\" ASC LIMIT @__pageSize OFFSET 0\";", text);
    }

    [Fact]
    public void IncludeDeletedPagedSelectIsUnfiltered_Sqlite()
    {
        // Combined paging + IncludeDeleted: ORDER BY/LIMIT present, soft-delete filter suppressed.
        var result = RunGenerator(WidgetStore("""
            [InquirySelectAll(OrderBy = "Id ASC", Paged = true, IncludeDeleted = true)]
            public partial Task<IReadOnlyList<Widget>> PageAllAsync(int offset, int limit, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("FROM \\\"TWidget\\\" ORDER BY \\\"Id\\\" ASC LIMIT @__limit OFFSET @__offset", text);
        Assert.DoesNotContain("\\\"IsDeleted\\\" = 0", text);
    }

    [Fact]
    public void PostgreSqlUsesTrueFalseLiterals()
    {
        var result = RunGenerator(WidgetStore(CrudMethods), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("WHERE \\\"IsDeleted\\\" = FALSE", text);
        Assert.Contains("SET \\\"IsDeleted\\\" = TRUE WHERE", text);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void MySqlUsesBacktickIdentifiers(string dialect)
    {
        var result = RunGenerator(WidgetStore(CrudMethods), dialect: dialect);
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("FROM `TWidget` WHERE `IsDeleted` = 0", text);
        Assert.Contains("UPDATE `TWidget` SET `IsDeleted` = 1 WHERE `Id` = @Id", text);
    }

    [Fact]
    public void OracleComposesSoftDeleteFilterUnquoted()
    {
        // Oracle's SELECT overrides (added by the Oracle provider) must also compose the filter.
        var result = RunGenerator(WidgetStore(CrudMethods), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("FROM TWidget WHERE IsDeleted = 0", text);
        Assert.Contains("WHERE Id = :iq1$Idxxxx$30d4cf864d6e68 AND IsDeleted = 0", text);
        Assert.Contains("WHERE Name = :iq1$Namexx$ce0862aa45f482 AND IsDeleted = 0", text);
    }

    [Fact]
    public void TimestampFormFiltersIsNullAndStampsOnDelete_SqlServer()
    {
        const string tsEntity = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDoc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("DeletedAt"), InquirySoftDelete]
                public DateTime? DeletedAt { get; set; }
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Doc>> SelectAllAsync(CancellationToken cancellationToken = default);

                [InquiryDelete]
                public partial Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(tsEntity, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("WHERE [DeletedAt] IS NULL", text);
        Assert.Contains("SET [DeletedAt] = GETUTCDATE() WHERE [Id] = @Id", text);
    }

    [Fact]
    public void MultipleSoftDeleteColumnsReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TBad")]
            public sealed class Bad
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("A"), InquirySoftDelete]
                public bool A { get; set; }

                [InquiryColumn("B"), InquirySoftDelete]
                public bool B { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ033" || d.Id == "INQ034");
    }
}
