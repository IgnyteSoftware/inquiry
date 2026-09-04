using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string MultipleUnsupportedOperationsSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TWidget")]
        public sealed class Widget
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }

        public partial class WidgetStore : InquiryStore<Widget>
        {
            [InquiryDelete(ReturnEntity = true)]
            public partial Task<Widget?> DeleteFirstAsync(long id, CancellationToken cancellationToken = default);

            [InquiryDelete(ReturnEntity = true)]
            public partial Task<Widget?> DeleteSecondAsync(long id, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void UnsupportedOperationFailsCompilationByDefaultWithoutRuntimeStub()
    {
        var result = RunGenerator(DeleteReturningSource, dialect: "SqlServer");

        Assert.Contains(result.RunResult.Diagnostics,
            static diagnostic => diagnostic.Id == "INQ039" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("throw new global::System.NotSupportedException", store, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReportDiagnostic.Warn, DiagnosticSeverity.Warning)]
    [InlineData(ReportDiagnostic.Info, DiagnosticSeverity.Info)]
    [InlineData(ReportDiagnostic.Hidden, DiagnosticSeverity.Hidden)]
    public void ProjectWideLoweringEmitsCompileSafeRuntimeStub(
        ReportDiagnostic action,
        DiagnosticSeverity expectedSeverity)
    {
        var result = RunGenerator(
            DeleteReturningSource,
            dialect: "SqlServer",
            unsupportedOperationSeverity: action);

        Assert.Contains(result.RunResult.Diagnostics,
            diagnostic => diagnostic.Id == "INQ039" && diagnostic.Severity == expectedSeverity);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("throw new global::System.NotSupportedException", store, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectWideSuppressionEmitsCompileSafeRuntimeStub()
    {
        var result = RunGenerator(
            DeleteReturningSource,
            dialect: "SqlServer",
            unsupportedOperationSeverity: ReportDiagnostic.Suppress);

        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("throw new global::System.NotSupportedException", store, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultProjectPolicyRejectsEveryUnsupportedMethodWithoutStubs()
    {
        var result = RunGenerator(MultipleUnsupportedOperationsSource, dialect: "SqlServer");

        Assert.Equal(2, result.RunResult.Diagnostics.Count(static diagnostic =>
            diagnostic.Id == "INQ039" && diagnostic.Severity == DiagnosticSeverity.Error));
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("throw new global::System.NotSupportedException", store, StringComparison.Ordinal);
        Assert.Contains(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ProjectWideLoweringOptsEveryUnsupportedMethodIntoStubs()
    {
        var result = RunGenerator(
            MultipleUnsupportedOperationsSource,
            dialect: "SqlServer",
            unsupportedOperationSeverity: ReportDiagnostic.Warn);

        Assert.Equal(2, result.RunResult.Diagnostics.Count(static diagnostic => diagnostic.Id == "INQ039"));
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(2, CountOccurrences(store, "throw new global::System.NotSupportedException"));
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EditorConfigProjectWideWarningOptsEveryUnsupportedMethodIntoStubs()
    {
        var result = RunGenerator(
            MultipleUnsupportedOperationsSource,
            dialect: "SqlServer",
            syntaxTreeOptionsProvider: TestSyntaxTreeOptionsProvider.Uniform(ReportDiagnostic.Warn));

        Assert.Equal(2, result.RunResult.Diagnostics.Count(static diagnostic => diagnostic.Id == "INQ039"));
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(2, CountOccurrences(store, "throw new global::System.NotSupportedException"));
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EditorConfigProjectWideNoneOptsEveryUnsupportedMethodIntoStubs()
    {
        var result = RunGenerator(
            MultipleUnsupportedOperationsSource,
            dialect: "SqlServer",
            syntaxTreeOptionsProvider: new TestSyntaxTreeOptionsProvider(
                static _ => null,
                globalAction: ReportDiagnostic.Suppress));

        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Equal(2, CountOccurrences(store, "throw new global::System.NotSupportedException"));
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EditorConfigMixedTreeSettingsKeepDefaultErrorsAndDoNotEmitStubs()
    {
        var result = RunGenerator(
            MultipleUnsupportedOperationsSource,
            dialect: "SqlServer",
            syntaxTreeOptionsProvider: new TestSyntaxTreeOptionsProvider(static tree =>
                tree.GetText().ToString().Contains("WidgetStore", StringComparison.Ordinal)
                    ? ReportDiagnostic.Error
                    : ReportDiagnostic.Warn));

        Assert.Equal(2, result.RunResult.Diagnostics.Count(static diagnostic =>
            diagnostic.Id == "INQ039" && diagnostic.Severity == DiagnosticSeverity.Error));
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("throw new global::System.NotSupportedException", store, StringComparison.Ordinal);
        Assert.Contains(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private sealed class TestSyntaxTreeOptionsProvider : SyntaxTreeOptionsProvider
    {
        private readonly Func<SyntaxTree, ReportDiagnostic?> _getAction;
        private readonly ReportDiagnostic? _globalAction;

        public TestSyntaxTreeOptionsProvider(
            Func<SyntaxTree, ReportDiagnostic?> getAction,
            ReportDiagnostic? globalAction = null)
        {
            _getAction = getAction;
            _globalAction = globalAction;
        }

        public static TestSyntaxTreeOptionsProvider Uniform(ReportDiagnostic action)
            => new(_ => action);

        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken)
            => GeneratedKind.NotGenerated;

        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            var action = diagnosticId == "INQ039" ? _getAction(tree) : null;
            severity = action.GetValueOrDefault();
            return action.HasValue;
        }

        public override bool TryGetGlobalDiagnosticValue(
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            var action = diagnosticId == "INQ039" ? _globalAction : null;
            severity = action.GetValueOrDefault();
            return action.HasValue;
        }
    }

}
