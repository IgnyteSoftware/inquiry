using Inquiry.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Inquiry.Generators.Tests;

/// <summary>
/// INQ048 (<see cref="InquiryRawSqlAnalyzer"/>): non-constant command text passed to
/// <c>InquiryCommand</c> warns; compile-time-constant text (literals, consts, nameof,
/// constant concatenation) and non-string arguments stay silent.
/// </summary>
public sealed class RawSqlAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body)
    {
        var source = $$"""
            using Inquiry.Commands;

            public static class Demo
            {
                public const string ConstSql = "SELECT 1";

                public static void Run(string userInput)
                {
                    {{body}}
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "RawSqlAnalyzerTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new MetadataReference[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(typeof(System.Data.Common.DbCommand).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(global::Inquiry.Commands.InquiryCommand).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new InquiryRawSqlAnalyzer()));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Theory]
    [InlineData("""var c = new InquiryCommand("SELECT * FROM T");""")]
    [InlineData("""var c = new InquiryCommand(ConstSql);""")]
    [InlineData("""var c = new InquiryCommand("SELECT * FROM " + nameof(Demo));""")]
    [InlineData("""var c = new InquiryCommand(ConstSql, null, 30);""")]
    public async Task ConstantCommandTextIsSilent(string body)
    {
        var diagnostics = await AnalyzeAsync(body);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("""var c = new InquiryCommand("SELECT * FROM T WHERE X = '" + userInput + "'");""")]
    [InlineData("""var c = new InquiryCommand($"SELECT * FROM T WHERE X = '{userInput}'");""")]
    [InlineData("""var c = new InquiryCommand(userInput);""")]
    public async Task NonConstantCommandTextWarnsINQ048(string body)
    {
        var diagnostics = await AnalyzeAsync(body);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("INQ048", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public async Task BinderOverloadStillChecksOnlyTheCommandText()
    {
        // The (string, Action<DbCommand>) overload: a non-constant binder is fine; the text rules.
        var diagnostics = await AnalyzeAsync(
            """var c = new InquiryCommand(ConstSql, cmd => { _ = cmd; });""");
        Assert.Empty(diagnostics);

        var warned = await AnalyzeAsync(
            """var c = new InquiryCommand("SELECT " + userInput, cmd => { _ = cmd; });""");
        Assert.Single(warned, d => d.Id == "INQ048");
    }
}
