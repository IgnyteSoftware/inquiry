using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Benchmarks.Generators;

[MemoryDiagnoser]
public class GeneratorBenchmarks
{
    [Params(25, 100, 500)]
    public int EntityCount { get; set; }

    private GeneratorFixture _fixture = null!;
    private GeneratorDriver _primed = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = new GeneratorFixture(EntityCount);
        _primed = _fixture.CreateDriver().RunGenerators(_fixture.Original);
        _fixture.Verify(_primed, _fixture.Original);
        foreach (var edited in new[] { _fixture.Unrelated, _fixture.Model, _fixture.RelationFilter })
        {
            var reused = _primed.RunGenerators(edited);
            _fixture.Verify(reused, edited);
            if (!GeneratorFixture.Snapshot(reused).SequenceEqual(GeneratorFixture.Snapshot(_fixture.CreateDriver().RunGenerators(edited))))
                throw new InvalidOperationException("Reused-driver output differs from fresh generation.");
        }
        Console.WriteLine($"Verified {EntityCount} entities; generated output count: {GeneratorFixture.Snapshot(_primed).Length}.");
    }

    [Benchmark]
    public GeneratorDriver FreshGeneration() => _fixture.CreateDriver().RunGenerators(_fixture.Original);

    // Keep the primed driver unchanged so each invocation measures an edit, not a cache hit.
    [Benchmark]
    public GeneratorDriver UnrelatedEdit() => _primed.RunGenerators(_fixture.Unrelated);

    [Benchmark]
    public GeneratorDriver ModelEdit() => _primed.RunGenerators(_fixture.Model);

    [Benchmark]
    public GeneratorDriver RelationFilterEdit() => _primed.RunGenerators(_fixture.RelationFilter);
}

internal sealed class GeneratorFixture
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
    public Dictionary<string, string> Sources { get; }
    public CSharpCompilation Original { get; }
    public CSharpCompilation Unrelated { get; }
    public CSharpCompilation Model { get; }
    public CSharpCompilation RelationFilter { get; }

    public GeneratorFixture(int entityCount)
    {
        Sources = new Dictionary<string, string>
        {
            ["Globals.cs"] = "global using System.Collections.Generic; global using System.Threading; global using System.Threading.Tasks; global using Inquiry.Entities; global using Inquiry.Stores; [assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]",
            ["Unrelated.cs"] = "namespace Consumer; public static class Unrelated { public const int Value = 1; }"
        };
        for (var index = 0; index < entityCount; index++)
        {
            var relation = index == 0 ? "[InquiryRelation(nameof(Model1.ParentId))] public List<Model1> Children { get; set; } = new();" : "";
            Sources[$"Model{index}.cs"] = $$"""
                namespace Consumer;
                [InquiryTable("Rows{{index}}")]
                public sealed class Model{{index}}
                {
                    [InquiryKey] public int Id { get; set; }
                    [InquiryColumn] public int ParentId { get; set; }
                    [InquiryColumn, InquiryGlobalFilter] public bool Active { get; set; }
                    [InquiryColumn] public string Name { get; set; } = "";
                    {{relation}}
                }
                public partial class Store{{index}} : InquiryStore<Model{{index}}>
                {
                    [InquirySelectAllEager] public partial IAsyncEnumerable<Model{{index}}> AllAsync(CancellationToken cancellationToken = default);
                }
                """;
        }
        var trees = Sources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => CSharpSyntaxTree.ParseText(pair.Value, ParseOptions, pair.Key));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[]
            {
                typeof(Inquiry.Entities.InquiryTableAttribute).Assembly.Location,
                typeof(Inquiry.Sqlite.DependencyInjection.SqliteInquiryServiceCollectionExtensions).Assembly.Location,
                typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location
            }).Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path));
        Original = CSharpCompilation.Create("GeneratorConsumer", trees.Values, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Unrelated = Edit("Unrelated.cs", Sources["Unrelated.cs"].Replace("Value = 1", "Value = 2"));
        Model = Edit("Model1.cs", Sources["Model1.cs"].Replace("[InquiryColumn] public string Name", "[InquiryColumn(\"DisplayName\")] public string Name"));
        RelationFilter = Edit("Model1.cs", Sources["Model1.cs"].Replace("[InquiryColumn] public int ParentId", "[InquiryColumn(\"OwnerId\")] public int ParentId")
            .Replace("InquiryGlobalFilter]", "InquiryGlobalFilter(KeepWhen = false)]"));

        CSharpCompilation Edit(string path, string source) => Original.ReplaceSyntaxTree(trees[path], CSharpSyntaxTree.ParseText(source, ParseOptions, path));
    }

    public GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        new[] { new Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);

    public void Verify(GeneratorDriver driver, CSharpCompilation input)
    {
        var result = driver.GetRunResult();
        var errors = result.Diagnostics.Concat(input.AddSyntaxTrees(result.GeneratedTrees).GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0 || result.Results.Any(generator => generator.Exception is not null) || result.GeneratedTrees.Length == 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
    }

    public static string[] Snapshot(GeneratorDriver driver) => driver.GetRunResult().Results
        .SelectMany(result => result.GeneratedSources.Select(source => source.HintName + "\n" + source.SourceText))
        .Order(StringComparer.Ordinal).ToArray();
}
