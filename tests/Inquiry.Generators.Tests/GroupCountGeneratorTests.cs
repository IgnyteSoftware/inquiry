using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// GroupCount emission tests: <c>[InquiryGroupCount("col")]</c> emits a
/// <c>SELECT col, COUNT(*) FROM t GROUP BY col</c> const and generates an inline
/// materializer struct for <c>GroupCount&lt;TKey&gt;</c>.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string GroupCountEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TOrder")]
        public sealed class Order2
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Status")]
            public string Status { get; set; } = string.Empty;

            [InquiryColumn("Priority")]
            public int Priority { get; set; }
        }
        """;

    private static string Order2Store(string methods) =>
        GroupCountEntity + "\n\npublic partial class Order2Store : Inquiry.Stores.InquiryStore<Demo.Order2>\n{\n" + methods + "\n}\n";

    private static string GetOrder2Store(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("Order2Store.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void GroupCountEmitsGroupBySql_Sqlite()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT \\\"Status\\\", COUNT(*) FROM \\\"TOrder\\\" GROUP BY \\\"Status\\\"", text);
        Assert.Contains("_sqlGroupCount_CountByStatusAsync", text);
    }

    [Fact]
    public void GroupCountEmitsInlineMaterializer()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("_GroupCountMat_CountByStatusAsync", text);
        Assert.Contains("IInquiryEntityMaterializer<global::Inquiry.GroupCount<string>>", text);
        Assert.Contains("GetInt64(1)", text);
    }

    [Fact]
    public void GroupCountWithIntKey_Sqlite()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Priority")]
            public partial Task<IReadOnlyList<GroupCount<int>>> CountByPriorityAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT \\\"Priority\\\", COUNT(*) FROM \\\"TOrder\\\" GROUP BY \\\"Priority\\\"", text);
        Assert.Contains("reader.GetInt32(0)", text);
    }

    [Fact]
    public void GroupCount_SqlServer()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT [Status], COUNT_BIG(*) FROM [TOrder] GROUP BY [Status]", text);
        Assert.Contains("reader.GetString(0)", text);
        Assert.Contains("reader.GetInt64(1)", text);
    }

    [Theory]
    [InlineData("Sqlite", "COUNT(*)")]
    [InlineData("PostgreSql", "COUNT(*)")]
    [InlineData("MySql", "COUNT(*)")]
    [InlineData("MariaDb", "COUNT(*)")]
    [InlineData("Oracle", "COUNT(*)")]
    [InlineData("SqlServer", "COUNT_BIG(*)")]
    public void GroupCountUsesProviderCountAndTypedMaterializer(string dialect, string countExpression)
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Priority")]
            public partial Task<IReadOnlyList<GroupCount<int>>> CountByPriorityAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains(countExpression, text);
        Assert.Contains("reader.GetInt32(0)", text);
        Assert.Contains("reader.GetInt64(1)", text);
        Assert.DoesNotContain("GetValue(", text);
        Assert.DoesNotContain("Convert.ChangeType", text);
    }

    [Fact]
    public void GroupCountUnknownColumnReportsINQ007()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Statuss")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ007" && d.Severity == DiagnosticSeverity.Error);
        // The method must be dropped rather than crashing the run: neither the GROUP BY const nor the
        // inline materializer is emitted, and the generator does not fault (CS8785).
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id == "CS8785");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static t => t.GetText().ToString().Contains("_sqlGroupCount_CountByStatusAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupCountNonGroupCountElementReportsINQ005()
    {
        // The element must be GroupCount<TKey>; a bare IReadOnlyList<string> leaves the generator with
        // no key type to materialize, so it is rejected as an unsupported return type up front.
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<string>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ005" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id == "CS8785");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static t => t.GetText().ToString().Contains("_sqlGroupCount_CountByStatusAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupCountUsesQueryListAsync()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("QueryListAsync", text);
    }
}
