using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

/// <summary>
/// #248: attribute strings and unsupported-operation reasons reach generated C# string literals and
/// XML documentation. Control characters, Unicode line separators, quotes and backslashes must
/// produce valid generated source that keeps the original value.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void ControlCharactersInIdentifiersEmitEscapedLiteralsAndSafeDocumentation()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("T\nWidget")]
            public sealed class Widget
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn("Na\rme\t\0\u0085\u2028\u2029 \"q\" \\s")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class WidgetStore : InquiryStore<Widget>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Widget>> AllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains(
            """private const string _sqlSelectAll = "SELECT \"Id\", \"Na\rme\t\0\u0085\u2028\u2029 \"\"q\"\" \\s\" FROM \"T\nWidget\"";""",
            store);
        Assert.Contains("    /// me\t" + """\0\u0085\u2028\u2029 ""q"" \s" FROM "T""", store);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Widget.InquiryEntity.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains(
            """public const string ColumnList = "\"Id\", \"Na\rme\t\0\u0085\u2028\u2029 \"\"q\"\" \\s\"";""",
            entity);
    }

    [Fact]
    public void LiteralEscapesEveryCharacterThatBreaksARegularStringLiteral()
    {
        Assert.Equal(
            "\"" + """a\"b\\c\td\ne\rf\0g\u0085h\u2028i\u2029j\u0001k""" + "\"",
            GeneratorHelpers.Literal("a\"b\\c\td\ne\rf\0g\u0085h\u2028i\u2029j\u0001k"));
        Assert.Equal("""a\"b\\c\n""", GeneratorHelpers.Escape("a\"b\\c\n"));
        Assert.Equal("null", GeneratorHelpers.Literal(null));
    }

    [Fact]
    public void UnsupportedStubReasonEmitsValidThrowAndDocumentation()
    {
        // No current provider reason contains these characters. This guards the stub emitter itself.
        var method = new StoreMethodData(
            "Run",
            StoreOperation.SelectAll,
            "global::System.Threading.Tasks.Task",
            EquatableArray<ParameterData>.Empty,
            EquatableArray<string>.Empty,
            EquatableArray<PredicateData>.Empty,
            EquatableArray<SetExpressionData>.Empty,
            ProcedureName: null,
            ReturnsEntity: false,
            ReturnsList: false,
            ProcedureReturnKind.None,
            Location: null);
        var source = new System.Text.StringBuilder();

        StoreOperationEmitter.EmitUnsupportedStub(source, method, "line1\r\nline2\u2028\0 \"q\" \\s");

        var text = source.ToString();
        Assert.Contains("""throw new global::System.NotSupportedException("line1\r\nline2\u2028\0 \"q\" \\s");""", text);
        Assert.Contains("""    /// line2\u2028\0 "q" \s</exception>""", text);
        var tree = CSharpSyntaxTree.ParseText("partial class C\n{\n" + text + "}\n");
        Assert.DoesNotContain(tree.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }
}
