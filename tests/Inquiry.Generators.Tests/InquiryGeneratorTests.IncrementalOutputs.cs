using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Pins which generator steps re-run after an edit (#182). Each edit starts from the same primed driver.
/// <c>Cached</c> means the step did not run for that item, so an unaffected materializer was not emitted again.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string C = nameof(IncrementalStepRunReason.Cached);
    private const string M = nameof(IncrementalStepRunReason.Modified);

    private static readonly Dictionary<string, string> _incrementalSources = new()
    {
        ["parent.cs"] = """
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
        ["isolated.cs"] = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Isolated")]
            public sealed class Isolated
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn] public string Name { get; set; } = "";
            }
            public partial class IsolatedStore : InquiryStore<Isolated>
            {
                [InquirySelectAll] public partial IAsyncEnumerable<Isolated> AllAsync(CancellationToken ct = default);
            }
            """,
        ["other.cs"] = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("Others")]
            public sealed class Other
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn] public string Name { get; set; } = "";
            }
            public partial class OtherStore : InquiryStore<Other>
            {
                [InquirySelectAll] public partial IAsyncEnumerable<Other> AllAsync(CancellationToken ct = default);
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

                [InquirySelectAll]
                public partial Task<IReadOnlyList<Summary>> SummariesAsync(CancellationToken ct = default);
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
        ["unrelated.cs"] = "namespace Other; public static class Unrelated { public const int Value = 1; }",
        ["dialect.cs"] = "[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]",
    };

    public static TheoryData<string> IncrementalEdits => new()
    {
        "unrelated", "isolated entity", "related child", "store", "projection", "dto",
    };

    [Theory]
    [MemberData(nameof(IncrementalEdits))]
    public void ModelEditReEmitsOnlyTheMaterializersItChanges(string edit)
    {
        var (reused, fresh) = RunIncrementalEdit(edit);
        var result = reused.GetRunResult().Results[0];

        Assert.Null(result.Exception);
        Assert.Equal(SourceSnapshot(fresh), SourceSnapshot(reused));

        var entities = new Dictionary<string, string> { ["Parent"] = C, ["Child"] = C, ["Isolated"] = C, ["Other"] = C };
        if (edit == "isolated entity")
        {
            entities["Isolated"] = M;
        }
        else if (edit == "related child")
        {
            // The parent's materializer reads only the parent's own columns, so editing its child leaves it cached.
            entities["Child"] = M;
        }

        Assert.Equal(entities, StepReasons(result, InquiryGeneratorBase.ResolvedEntitiesTrackingName));
        Assert.Equal(entities, StepReasons(result, InquiryGeneratorBase.EntityMaterializersTrackingName));
        Assert.Equal(entities, OutputReasons(result, InquiryGeneratorBase.EntityMaterializersTrackingName));

        var projections = new Dictionary<string, string> { ["Summary"] = edit == "projection" ? M : C };
        Assert.Equal(projections, StepReasons(result, InquiryGeneratorBase.ProjectionMaterializersTrackingName));
        Assert.Equal(projections, OutputReasons(result, InquiryGeneratorBase.ProjectionMaterializersTrackingName));

        var adHocs = new Dictionary<string, string> { ["Report"] = edit == "dto" ? M : C };
        Assert.Equal(adHocs, StepReasons(result, InquiryGeneratorBase.AdHocMaterializersTrackingName));
        Assert.Equal(adHocs, OutputReasons(result, InquiryGeneratorBase.AdHocMaterializersTrackingName));

        // Stores, schema and registration still share one output that re-runs on any model edit. Only an
        // unrelated edit leaves it cached.
        var model = new Dictionary<string, string> { ["model"] = edit == "unrelated" ? C : M };
        Assert.Equal(model, OutputReasons(result, InquiryGeneratorBase.ModelOutputTrackingName));
    }

    [Fact]
    public void DuplicateEntityDeclarationKeepsOneMaterializer()
    {
        // The attribute on two partial parts is already a compile error (CS0579). The generator must still
        // emit one materializer for the type instead of failing on a duplicate hint name.
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            [InquiryTable("Widgets")]
            public sealed partial class Widget
            {
                [InquiryKey] public int Id { get; set; }
            }
            [InquiryTable("Widgets")]
            public sealed partial class Widget
            {
                [InquiryColumn] public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.All(result.RunResult.Results, run => Assert.Null(run.Exception));
        Assert.Single(result.RunResult.GeneratedTrees, tree => tree.FilePath.EndsWith("Widget.InquiryEntity.g.cs", StringComparison.Ordinal));
    }

    private static (GeneratorDriver Reused, GeneratorDriver Fresh) RunIncrementalEdit(string edit)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var trees = _incrementalSources.ToDictionary(pair => pair.Key,
            pair => CSharpSyntaxTree.ParseText(pair.Value, parseOptions, pair.Key));
        var original = CSharpCompilation.Create("IncrementalOutputs", trees.Values, GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
            new[] { new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        var primed = CreateDriver().RunGeneratorsAndUpdateCompilation(original, out var primedOutput, out var primedDiagnostics);
        Assert.Empty(primedDiagnostics);
        Assert.Empty(primedOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var (path, from, to) = edit switch
        {
            "unrelated" => ("unrelated.cs", "Value = 1", "Value = 2"),
            "isolated entity" => ("isolated.cs", "[InquiryColumn] public string Name", "[InquiryColumn(\"DisplayName\")] public string Name"),
            "related child" => ("child.cs", "[InquiryKey] public int Id", "[InquiryKey(\"DatabaseKey\")] public int Id"),
            "store" => ("stores.cs", "Parent> AllAsync", "Parent> EverythingAsync"),
            "projection" => ("projection.cs", "[InquiryColumn] public int Id", "[InquiryColumn] public bool Active"),
            "dto" => ("dto.cs", "public int Count", "public long Count"),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };
        var source = _incrementalSources[path];
        Assert.Contains(from, source, StringComparison.Ordinal);
        var edited = original.ReplaceSyntaxTree(trees[path],
            CSharpSyntaxTree.ParseText(source.Replace(from, to, StringComparison.Ordinal), parseOptions, path));

        var reused = primed.RunGeneratorsAndUpdateCompilation(edited, out var reusedOutput, out var reusedDiagnostics);
        Assert.Empty(reusedDiagnostics);
        Assert.Empty(reusedOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return (reused, CreateDriver().RunGenerators(edited));
    }

    private static Dictionary<string, string> StepReasons(GeneratorRunResult result, string trackingName)
    {
        Assert.True(result.TrackedSteps.TryGetValue(trackingName, out var steps), $"Expected a tracked step named '{trackingName}'.");
        return steps
            .SelectMany(step => step.Outputs)
            .Where(output => output.Value is not null)
            .ToDictionary(output => Label(output.Value), output => output.Reason.ToString());
    }

    private static Dictionary<string, string> OutputReasons(GeneratorRunResult result, string inputTrackingName)
    {
        var steps = result.TrackedOutputSteps.Values.SelectMany(static outputs => outputs)
            .Where(step => step.Inputs.Length > 0 && step.Inputs[0].Source.Name == inputTrackingName)
            .ToArray();
        Assert.NotEmpty(steps);
        var reasons = new Dictionary<string, string>();
        foreach (var step in steps)
        {
            var (source, index) = step.Inputs[0];
            var value = source.Outputs[index].Value;
            if (value is null)
            {
                continue;
            }

            Assert.Single(step.Outputs);
            reasons.Add(Label(value), step.Outputs[0].Reason.ToString());
        }

        return reasons;
    }

    private static string Label(object value) => value switch
    {
        ResolvedEntity resolved => resolved.Entity.Name,
        EntityData entity => entity.Name,
        ProjectionData projection => projection.Name,
        AdHocData adHoc => adHoc.Name,
        _ => "model",
    };
}
