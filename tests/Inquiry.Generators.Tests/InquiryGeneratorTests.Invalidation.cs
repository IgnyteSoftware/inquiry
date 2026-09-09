using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Theory]
    [InlineData("store removal")]
    [InlineData("entity removal")]
    [InlineData("related key")]
    [InlineData("filter")]
    [InlineData("projection")]
    [InlineData("dto")]
    [InlineData("dto removal")]
    [InlineData("dialect")]
    public void PrimedDriverMatchesFreshGenerationAfterDependencyEdit(string edit)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var sources = new Dictionary<string, string>
        {
            ["entities.cs"] = """
                using System.Collections.Generic;
                using Inquiry.Entities;
                namespace Demo;
                [InquiryTable("Parents")]
                public sealed class Parent
                {
                    [InquiryKey] public int Id { get; set; }
                    [InquiryColumn, InquiryGlobalFilter] public bool Active { get; set; }
                    [InquiryRelation(nameof(Child.ParentId))]
                    public List<Child> Children { get; set; } = new();
                }
                """,
            ["child.cs"] = """
                using Inquiry.Entities;
                namespace Demo;
                [InquiryTable("Children")]
                public sealed class Child
                {
                    [InquiryKey] public int Id { get; set; }
                    [InquiryColumn] public int ParentId { get; set; }
                }
                """,
            ["stores.cs"] = """
                global using System.Threading.Tasks;
                using System.Collections.Generic;
                using System.Threading;
                using Inquiry.Stores;
                namespace Demo;
                public partial class ParentStore : InquiryStore<Parent>
                {
                    [InquirySelectAllEager]
                    public partial IAsyncEnumerable<Parent> AllAsync(CancellationToken ct = default);
                }
                """,
            ["projection.cs"] = """
                using Inquiry.Entities;
                namespace Demo;
                [InquiryProjection(typeof(Parent))]
                public sealed class Summary
                {
                    [InquiryColumn] public int Id { get; set; }
                }
                """,
            ["dto.cs"] = """
                using Inquiry.Entities;
                namespace Demo;
                [InquiryAdHoc]
                public sealed class Report { public int Count { get; set; } }
                """,
            ["removed.cs"] = """
                using Inquiry.Entities;
                namespace Demo;
                [InquiryTable("Unused")]
                public sealed class Unused { [InquiryKey] public int Id { get; set; } }
                """,
            ["dialect.cs"] = "[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]"
        };
        var trees = sources.ToDictionary(pair => pair.Key,
            pair => CSharpSyntaxTree.ParseText(pair.Value, parseOptions, pair.Key));
        var original = CSharpCompilation.Create("InvalidationConsumer", trees.Values, GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
            new ISourceGenerator[]
            {
                new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator(),
                new global::Inquiry.SqlServer.Analyzer.InquirySqlServerGenerator().AsSourceGenerator()
            }, parseOptions: parseOptions);

        var primed = CreateDriver().RunGeneratorsAndUpdateCompilation(original, out var originalOutput, out _);
        Assert.Empty(primed.GetRunResult().Diagnostics);
        Assert.Empty(originalOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var originalSources = SourceSnapshot(primed);
        var path = edit switch
        {
            "store removal" => "stores.cs",
            "entity removal" => "removed.cs",
            "related key" => "child.cs",
            "filter" => "entities.cs",
            "projection" => "projection.cs",
            "dto" or "dto removal" => "dto.cs",
            _ => "dialect.cs"
        };
        var replacement = edit switch
        {
            "store removal" or "entity removal" or "dto removal" => "",
            "related key" => sources[path].Replace("[InquiryKey] public int Id", "[InquiryKey(\"DatabaseKey\")] public int Id"),
            "filter" => sources[path].Replace("InquiryGlobalFilter]", "InquiryGlobalFilter(KeepWhen = false)]"),
            "projection" => sources[path].Replace("[InquiryColumn] public int Id", "[InquiryColumn] public bool Active"),
            "dto" => sources[path].Replace("public int Count", "public long Count"),
            _ => sources[path].Replace("Sqlite", "SqlServer")
        };
        var edited = original.ReplaceSyntaxTree(trees[path], CSharpSyntaxTree.ParseText(replacement, parseOptions, path));
        var reused = primed.RunGeneratorsAndUpdateCompilation(edited, out var reusedOutput, out var reusedDiagnostics);
        var fresh = CreateDriver().RunGeneratorsAndUpdateCompilation(edited, out var freshOutput, out var freshDiagnostics);

        Assert.All(reused.GetRunResult().Results, result => Assert.Null(result.Exception));
        Assert.Equal(SourceSnapshot(fresh), SourceSnapshot(reused));
        Assert.Equal(freshDiagnostics.Select(diagnostic => diagnostic.ToString()).Order(),
            reusedDiagnostics.Select(diagnostic => diagnostic.ToString()).Order());
        Assert.Equal(freshOutput.GetDiagnostics().Select(diagnostic => diagnostic.ToString()).Order(),
            reusedOutput.GetDiagnostics().Select(diagnostic => diagnostic.ToString()).Order());
        Assert.Empty(reusedOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.False(originalSources.SequenceEqual(SourceSnapshot(reused)), "The edit must change generated output.");

        var outputText = string.Join("\n", SourceSnapshot(reused));
        var removedName = edit switch
        {
            "store removal" => "ParentStore",
            "entity removal" => "Unused",
            "dto removal" => "Report",
            _ => null
        };
        if (removedName is not null)
            Assert.DoesNotContain(removedName, outputText, StringComparison.Ordinal);
        if (edit == "dto")
            Assert.Contains("Count = reader.GetInt64(0)", outputText, StringComparison.Ordinal);
        if (edit == "related key")
        {
            var parentStore = Assert.Single(SourceSnapshot(reused), source => source.Contains("ParentStore.InquiryStore.g.cs", StringComparison.Ordinal));
            Assert.Contains("DatabaseKey", parentStore, StringComparison.Ordinal);
        }
    }

    private static string[] SourceSnapshot(GeneratorDriver driver) => driver.GetRunResult().Results
        .SelectMany(result => result.GeneratedSources.Select(source =>
            result.Generator.GetType().FullName + "/" + source.HintName + "\n" + source.SourceText))
        .Order(StringComparer.Ordinal).ToArray();
}
