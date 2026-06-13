using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Existence tests ([InquiryExists]): emits <c>SELECT CASE WHEN EXISTS(SELECT 1 FROM … WHERE …) THEN 1
/// ELSE 0 END</c> returned as <c>Task&lt;bool&gt;</c>. With no criteria it tests the whole table; with
/// [InquiryWhere] criteria it tests for a match (binding through the predicate closure). Verifies the
/// per-dialect form (Oracle's FROM DUAL), the active-row filter composition, and the bool return path.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ExistsWidget = """
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
        }
        """;

    private static string ExistsWidgetStore(string methods) =>
        ExistsWidget + "\n\npublic partial class WidgetStore : Inquiry.Stores.InquiryStore<Demo.Widget>\n{\n" + methods + "\n}\n";

    private static string GetExistsWidgetStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void ExistsWithNoCriteriaTestsWholeTable_Sqlite()
    {
        var result = RunGenerator(ExistsWidgetStore("""
            [InquiryExists]
            public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetExistsWidgetStore(result);

        Assert.Contains("_sqlExists_AnyAsync = \"SELECT CASE WHEN EXISTS(SELECT 1 FROM \\\"TWidget\\\") THEN 1 ELSE 0 END\";", text);
        // No criteria → no binder, command returned directly.
        Assert.Contains("Inquiry.ExecuteScalarAsync<bool>(new global::Inquiry.Commands.InquiryCommand(_sqlExists_AnyAsync)", text);
    }

    [Fact]
    public void ExistsWithCriteriaTestsForMatch_Sqlite()
    {
        var result = RunGenerator(ExistsWidgetStore("""
            [InquiryExists]
            [InquiryWhere("Name")]
            public partial Task<bool> ByNameAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetExistsWidgetStore(result);

        Assert.Contains("_sqlExists_ByNameAsync = \"SELECT CASE WHEN EXISTS(SELECT 1 FROM \\\"TWidget\\\" WHERE \\\"Name\\\" = @Name) THEN 1 ELSE 0 END\";", text);
        Assert.Contains("Inquiry.ExecuteScalarAsync<bool>(_cmd", text);
    }

    [Fact]
    public void ExistsComposesActiveRowFilter_Sqlite()
    {
        // A soft-delete entity excludes hidden rows from the existence test.
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
                [InquiryColumn("IsDeleted"), InquirySoftDelete] public bool IsDeleted { get; set; }
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryExists]
                public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("SELECT CASE WHEN EXISTS(SELECT 1 FROM \\\"TDoc\\\" WHERE \\\"IsDeleted\\\" = 0) THEN 1 ELSE 0 END", text);
    }

    [Fact]
    public void OracleExistsSelectsFromDual()
    {
        var result = RunGenerator(ExistsWidgetStore("""
            [InquiryExists]
            public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetExistsWidgetStore(result);

        Assert.Contains("SELECT CASE WHEN EXISTS(SELECT 1 FROM TWidget) THEN 1 ELSE 0 END FROM DUAL", text);
    }

    [Fact]
    public void ExistsWithWrongReturnTypeReportsDiagnostic()
    {
        // [InquiryExists] must return Task<bool>.
        var result = RunGenerator(ExistsWidgetStore("""
            [InquiryExists]
            public partial Task<long> AnyAsync(CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
