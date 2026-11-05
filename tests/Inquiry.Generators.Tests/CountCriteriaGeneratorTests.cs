using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// [InquiryCount] with [InquiryWhere] criteria (#537): the count applies its criteria, whether they take a
/// value or not, and whether they are declared directly or through an [InquirySpecification] attribute.
/// Before the fix a parameterless criterion compiled and counted every row.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string CountCriteriaEntity = """
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
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;
            [InquiryColumn("Category")] public string? Category { get; set; }
        }

        [InquirySpecification]
        [InquiryWhere("Category", Compare.IsNull)]
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class UncategorizedAttribute : Attribute
        {
        }

        [InquirySpecification]
        [InquiryWhere("Category")]
        [AttributeUsage(AttributeTargets.Method)]
        public sealed class InCategoryAttribute : Attribute
        {
        }

        """;

    private static string CountCriteriaStore(string methods) =>
        CountCriteriaEntity + "public partial class WidgetStore : InquiryStore<Widget>\n{\n" + methods + "\n}\n";

    private static string CountCriteriaStoreText(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void CountAppliesParameterlessCriterion()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<long> CountUncategorizedAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = CountCriteriaStoreText(result);

        Assert.Contains("private const string _sqlCountFor_CountUncategorizedAsync = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" IS NULL\";", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long, byte>(new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlCountFor_CountUncategorizedAsync, default, static (_, _) => { }), cancellationToken);", text);
        Assert.DoesNotContain("_sqlCount =", text);
    }

    [Fact]
    public void CountAppliesValueCriterion()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [InquiryWhere("Name")]
            public partial Task<long> CountNamedAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = CountCriteriaStoreText(result);

        Assert.Contains("private const string _sqlCountFor_CountNamedAsync = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name\";", text);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<string>(", text);
        Assert.Contains("_sqlCountFor_CountNamedAsync,", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long, string>(_cmd, cancellationToken);", text);
    }

    [Fact]
    public void CountAppliesParameterlessSpecificationCriterion()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [Uncategorized]
            public partial Task<long> CountUncategorizedAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = CountCriteriaStoreText(result);

        Assert.Contains("private const string _sqlCountFor_CountUncategorizedAsync = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" IS NULL\";", text);
    }

    [Fact]
    public void CountAppliesValueSpecificationCriterion()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [InCategory]
            public partial Task<long> CountInCategoryAsync(string? category, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = CountCriteriaStoreText(result);

        Assert.Contains("private const string _sqlCountFor_CountInCategoryAsync = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" = @Category\";", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long, string?>(_cmd, cancellationToken);", text);
    }

    [Theory]
    [InlineData("Sqlite", "SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" IS NULL")]
    [InlineData("SqlServer", "SELECT COUNT_BIG(*) FROM [TWidget] WHERE [Category] IS NULL")]
    [InlineData("PostgreSql", "SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" IS NULL")]
    [InlineData("MySql", "SELECT COUNT(*) FROM `TWidget` WHERE `Category` IS NULL")]
    [InlineData("MariaDb", "SELECT COUNT(*) FROM `TWidget` WHERE `Category` IS NULL")]
    [InlineData("Oracle", "SELECT COUNT(*) FROM TWidget WHERE Category IS NULL")]
    public void CountParameterlessCriterionSqlPerDialect(string dialect, string expectedSql)
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [InquiryWhere("Category", Compare.IsNull)]
            public partial Task<long> CountUncategorizedAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);

        Assert.Contains($"private const string _sqlCountFor_CountUncategorizedAsync = \"{expectedSql}\";", CountCriteriaStoreText(result));
    }

    [Theory]
    [InlineData("Sqlite", "SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" = @Category")]
    [InlineData("SqlServer", "SELECT COUNT_BIG(*) FROM [TWidget] WHERE [Category] = @Category")]
    [InlineData("PostgreSql", "SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" = @Category")]
    [InlineData("MySql", "SELECT COUNT(*) FROM `TWidget` WHERE `Category` = @Category")]
    [InlineData("MariaDb", "SELECT COUNT(*) FROM `TWidget` WHERE `Category` = @Category")]
    // Oracle binds through its hashed parameter names.
    [InlineData("Oracle", "SELECT COUNT(*) FROM TWidget WHERE Category = :iq1$Catego$5b8aa3ad6018c5")]
    public void CountValueCriterionSqlPerDialect(string dialect, string expectedSql)
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            [InquiryWhere("Category")]
            public partial Task<long> CountInCategoryAsync(string? category, CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);

        Assert.Contains($"private const string _sqlCountFor_CountInCategoryAsync = \"{expectedSql}\";", CountCriteriaStoreText(result));
    }

    [Fact]
    public void CountCriterionComposesActiveRowFilter()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDoc")]
            public sealed class Doc
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn("Title")] public string? Title { get; set; }
                [InquiryColumn("IsDeleted"), InquirySoftDelete] public bool IsDeleted { get; set; }
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryCount]
                [InquiryWhere("Title", Compare.IsNull)]
                public partial Task<long> CountUntitledAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));

        Assert.Contains("private const string _sqlCountFor_CountUntitledAsync = \"SELECT COUNT(*) FROM \\\"TDoc\\\" WHERE \\\"Title\\\" IS NULL AND \\\"IsDeleted\\\" = 0\";", tree.GetText().ToString());
    }

    [Fact]
    public void CountWithCriterionAndIgnoredFilterEmitsOneConst()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TNote")]
            public sealed class Note
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn("Title")] public string? Title { get; set; }
                [InquiryColumn("IsLive"), InquiryGlobalFilter(Name = "Live")] public bool IsLive { get; set; }
            }

            public partial class NoteStore : InquiryStore<Note>
            {
                [InquiryCount]
                [InquiryIgnoreFilter("Live")]
                [InquiryWhere("Title", Compare.IsNull)]
                public partial Task<long> CountUntitledAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("NoteStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        const string constDeclaration = "private const string _sqlCountFor_CountUntitledAsync =";
        Assert.Equal(text.IndexOf(constDeclaration, StringComparison.Ordinal), text.LastIndexOf(constDeclaration, StringComparison.Ordinal));
        Assert.Contains("private const string _sqlCountFor_CountUntitledAsync = \"SELECT COUNT(*) FROM \\\"TNote\\\" WHERE \\\"Title\\\" IS NULL\";", text);
    }

    [Fact]
    public void FilteredAndPlainCountsCoexist()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

            [InquiryCount]
            [InquiryWhere("Category", Compare.IsNotNull)]
            public partial Task<long> CountCategorizedAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = CountCriteriaStoreText(result);

        Assert.Contains("private const string _sqlCount = \"SELECT COUNT(*) FROM \\\"TWidget\\\"\";", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long, byte>(new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlCount, default, static (_, _) => { }), cancellationToken);", text);
        Assert.Contains("private const string _sqlCountFor_CountCategorizedAsync = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"Category\\\" IS NOT NULL\";", text);
    }

    [Fact]
    public void PlainCountWithStrayParameterStillReportsInvalidParameters()
    {
        var result = RunGenerator(CountCriteriaStore("""
            [InquiryCount]
            public partial Task<long> CountAsync(string name, CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("INQ006", diagnostic.Id);
    }
}
