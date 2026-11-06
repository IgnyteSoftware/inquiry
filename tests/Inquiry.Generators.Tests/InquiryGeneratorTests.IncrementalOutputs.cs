using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Pins which generator steps re-run after an edit (#182). Each edit starts from the same primed driver.
/// <c>Cached</c> means the step did not run for that item, so an unaffected materializer or store was not
/// emitted again. <c>Unchanged</c> means the step ran and produced an equal value, so nothing after it ran.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string C = nameof(IncrementalStepRunReason.Cached);
    private const string U = nameof(IncrementalStepRunReason.Unchanged);
    private const string M = nameof(IncrementalStepRunReason.Modified);

    private static readonly string[] _entityLabels = ["Parent", "Child", "Isolated", "Other"];
    private static readonly string[] _storeLabels = ["IsolatedStore", "OtherStore", "ParentStore", "LockStore"];

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
        // SQLite has no row locking, so this method reports INQ039. The fixture lowers INQ039 to a warning,
        // which makes the generator emit a throwing stub for it.
        ["lock.cs"] = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Stores;
            namespace Demo;
            public partial class LockStore : InquiryStore<Other>
            {
                [InquirySelectAll(LockMode = InquiryLockMode.Update)]
                public partial Task<IReadOnlyList<Other>> LockedAsync(CancellationToken ct = default);
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
        "unrelated", "isolated entity", "related child", "store", "projection", "dto", "stub policy", "manifest",
    };

    [Theory]
    [MemberData(nameof(IncrementalEdits))]
    public void ModelEditReEmitsOnlyTheOutputsItChanges(string edit)
    {
        var (reused, fresh) = RunIncrementalEdit(edit);
        var result = reused.GetRunResult().Results[0];

        Assert.Null(result.Exception);
        AssertSameOutput(fresh, reused);

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

        // One letter per entity (Parent, Child, Isolated, Other), per store (IsolatedStore, OtherStore,
        // ParentStore, LockStore), or for a single value.
        var expected = edit switch
        {
            "unrelated" => new ExpectedReasons("CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "C", "C", "C", "C", "C"),
            "isolated entity" => new ExpectedReasons("UUMU", "CCUC", "MUUU", "MCCC", "MCCC", "UCCC", "M", "C", "C", "C", "M"),
            "related child" => new ExpectedReasons("MMUU", "UUCC", "UUMU", "CCMC", "CCMC", "CCUC", "M", "C", "C", "C", "M"),
            "store" => new ExpectedReasons("CCCC", "CCCC", "CCMC", "CCMC", "CCMC", "CCUC", "C", "C", "C", "C", "C"),
            "projection" => new ExpectedReasons("CCCC", "CCCC", "UUMU", "CCMC", "CCMC", "CCUC", "C", "M", "C", "C", "C"),
            "dto" => new ExpectedReasons("CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "C", "C", "C", "C", "C"),
            // The stub policy is project-wide, so every store slice changes. Only the store with a stubbed
            // method emits different text.
            "stub policy" => new ExpectedReasons("CCCC", "CCCC", "MMMM", "UUUM", "CCCM", "CCCU", "C", "C", "M", "U", "C"),
            "manifest" => new ExpectedReasons("CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "CCCC", "C", "C", "U", "M", "M"),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };

        Assert.Equal(Reasons(_entityLabels, expected.RelationSlices), StepReasons(result, InquiryGeneratorBase.RelationSlicesTrackingName));
        Assert.Equal(Reasons(_entityLabels, expected.RelationOutputs), OutputReasons(result, InquiryGeneratorBase.RelationSlicesTrackingName));
        Assert.Equal(Reasons(_storeLabels, expected.StoreSlices), StepReasons(result, InquiryGeneratorBase.StoreSlicesTrackingName));
        Assert.Equal(Reasons(_storeLabels, expected.StoreEmissions), StepReasons(result, InquiryGeneratorBase.StoreEmissionsTrackingName));
        Assert.Equal(Reasons(_storeLabels, expected.StoreOutputs), OutputReasons(result, InquiryGeneratorBase.StoreEmissionsTrackingName));
        Assert.Equal(Reasons(_storeLabels, expected.StoreFacts), StepReasons(result, InquiryGeneratorBase.StoreFactsTrackingName));
        Assert.Equal(Reasons(["model"], expected.EntityIndex), StepReasons(result, InquiryGeneratorBase.EntityIndexTrackingName));
        Assert.Equal(Reasons(["model"], expected.ProjectionIndex), StepReasons(result, InquiryGeneratorBase.ProjectionIndexTrackingName));
        Assert.Equal(Reasons(["model"], expected.StoreOptions), StepReasons(result, InquiryGeneratorBase.StoreOptionsTrackingName));
        Assert.Equal(Reasons(["model"], expected.SchemaOptions), StepReasons(result, InquiryGeneratorBase.SchemaOptionsTrackingName));
        Assert.Equal(Reasons(["model"], expected.Schema), OutputReasons(result, InquiryGeneratorBase.SchemaInputTrackingName));

        // Registration facts do not change for any of these edits, so registration never re-runs.
        Assert.Equal(Reasons(["model"], "C"), OutputReasons(result, InquiryGeneratorBase.RegistrationInputTrackingName));
    }

    [Fact]
    public void RelationEditReportsTheNewDiagnosticOnlyFromItsEntity()
    {
        // An unmapped foreign-key property breaks the parent's relation (INQ040). The child's own slice also
        // re-runs, but it has no relations, so its output reports nothing new.
        var (reused, fresh) = RunIncrementalEdit("child.cs", "[InquiryColumn] public int ParentId", "public int ParentId");
        var result = reused.GetRunResult().Results[0];

        Assert.Null(result.Exception);
        AssertSameOutput(fresh, reused);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "INQ040");
        Assert.Equal(Reasons(_entityLabels, "MUCC"), OutputReasons(result, InquiryGeneratorBase.RelationSlicesTrackingName));
    }

    public static TheoryData<string> DependencyEdits => new() { "junction", "procedure result", "computed child" };

    [Theory]
    [MemberData(nameof(DependencyEdits))]
    public void DependencyEditReEmitsTheStoreThatReadsIt(string edit)
    {
        // Each store reads an entity that is neither its own nor a plain relation child. Editing that entity
        // must re-emit the store, and the result must match a fresh run.
        var (sources, generator, path, from, to) = edit switch
        {
            "junction" => (new Dictionary<string, string>
            {
                ["model.cs"] = """
                    using System.Collections.Generic;
                    using Inquiry.Entities;
                    using Inquiry.Stores;
                    namespace Demo;
                    [InquiryTable("Orders")]
                    public sealed class Order
                    {
                        [InquiryKey] public long Id { get; set; }
                        [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]
                        public List<Product> Products { get; set; } = new();
                    }
                    [InquiryTable("Products")]
                    public sealed class Product { [InquiryKey] public long Id { get; set; } }
                    public partial class OrderStore : InquiryStore<Order>
                    {
                        [InquirySelectAllEager] public partial IAsyncEnumerable<Order> AllAsync(System.Threading.CancellationToken ct = default);
                    }
                    """,
                ["junction.cs"] = """
                    using Inquiry.Entities;
                    namespace Demo;
                    [InquiryTable("OrderProduct")]
                    public sealed class OrderProduct
                    {
                        [InquiryKey] public long OrderId { get; set; }
                        [InquiryKey] public long ProductId { get; set; }
                    }
                    """,
                ["dialect.cs"] = "[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]",
            }, (IIncrementalGenerator)new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator(), "junction.cs",
                "[InquiryKey] public long ProductId", "[InquiryKey(\"product_key\")] public long ProductId"),
            "procedure result" => (new Dictionary<string, string>
            {
                ["model.cs"] = """
                    using System.Collections.Generic;
                    using System.Threading;
                    using System.Threading.Tasks;
                    using Inquiry;
                    using Inquiry.Entities;
                    using Inquiry.Stores;
                    namespace Demo;
                    [InquiryTable("Order")]
                    public sealed class Order { [InquiryKey] public long Id { get; set; } }
                    public partial class OrderStore : InquiryStore<Order>
                    {
                        [InquiryStoredProcedure("usp_GetOrdersAndLines")]
                        public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetAsync(long id, CancellationToken ct = default);
                    }
                    """,
                ["line.cs"] = """
                    using Inquiry.Entities;
                    namespace Demo;
                    [InquiryTable("OrderLine")]
                    public sealed class OrderLine { [InquiryKey] public long Id { get; set; } }
                    """,
                ["dialect.cs"] = "[assembly: global::Inquiry.InquiryDialect(\"SqlServer\")]",
            }, new global::Inquiry.SqlServer.Analyzer.InquirySqlServerGenerator(), "line.cs",
                "[InquiryTable(\"OrderLine\")]", ""),
            "computed child" => (new Dictionary<string, string>
            {
                ["model.cs"] = """
                    using System.Collections.Generic;
                    using Inquiry.Entities;
                    using Inquiry.Stores;
                    namespace Demo;
                    [InquiryTable("Parents")]
                    public sealed class Parent
                    {
                        [InquiryKey] public int Id { get; set; }
                        [InquiryRelation(nameof(Child.ParentId))]
                        public List<Child> Children { get; set; } = new();
                    }
                    public partial class ParentStore : InquiryStore<Parent>
                    {
                        [InquirySelectAllEager] public partial IAsyncEnumerable<Parent> AllAsync(System.Threading.CancellationToken ct = default);
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
                        [InquiryColumn(Computed = "Id + 1")] public int Next { get; set; }
                    }
                    """,
                ["dialect.cs"] = "[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]",
            }, new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator(), "child.cs",
                "[InquiryColumn(Computed = \"Id + 1\")]",
                "[InquiryColumn(Computed = \"Id + 1\"), InquiryComputedExpression(\"sqlite\", \"Id + 2\"), InquiryComputedExpression(\"sqlite\", \"Id + 3\")]"),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };

        var (reused, fresh) = RunEdit(sources, generator, compilation => ReplaceText(compilation, path, from, to), requireClean: false);
        var result = reused.GetRunResult().Results[0];

        Assert.Null(result.Exception);
        AssertSameOutput(fresh, reused);
        var store = Assert.Single(StepReasons(result, InquiryGeneratorBase.StoreEmissionsTrackingName));
        Assert.Equal(M, store.Value);
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
        Func<CSharpCompilation, CSharpCompilation> apply = edit switch
        {
            "unrelated" => compilation => ReplaceText(compilation, "unrelated.cs", "Value = 1", "Value = 2"),
            "isolated entity" => compilation => ReplaceText(compilation, "isolated.cs", "[InquiryColumn] public string Name", "[InquiryColumn(\"DisplayName\")] public string Name"),
            "related child" => compilation => ReplaceText(compilation, "child.cs", "[InquiryKey] public int Id", "[InquiryKey(\"DatabaseKey\")] public int Id"),
            "store" => compilation => ReplaceText(compilation, "stores.cs", "Parent> AllAsync", "Parent> EverythingAsync"),
            "projection" => compilation => ReplaceText(compilation, "projection.cs", "[InquiryColumn] public int Id", "[InquiryColumn] public bool Active"),
            "dto" => compilation => ReplaceText(compilation, "dto.cs", "public int Count", "public long Count"),
            // INQ039 back at its default severity turns the stub off.
            "stub policy" => compilation => compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
                compilation.Options.SpecificDiagnosticOptions.SetItem("INQ039", ReportDiagnostic.Default))),
            // A hand-declared schema-manifest key makes the generator report INQ075 and drop its own manifest metadata.
            "manifest" => compilation => ReplaceText(compilation, "dialect.cs", "(\"Sqlite\")]",
                "(\"Sqlite\")]\n[assembly: System.Reflection.AssemblyMetadata(\"Inquiry.SchemaManifest.Version\", \"1\")]"),
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };

        return RunEdit(_incrementalSources, new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator(), apply,
            requireClean: edit is not ("stub policy" or "manifest"));
    }

    private static (GeneratorDriver Reused, GeneratorDriver Fresh) RunIncrementalEdit(string path, string from, string to)
        => RunEdit(_incrementalSources, new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator(),
            compilation => ReplaceText(compilation, path, from, to), requireClean: false);

    private static (GeneratorDriver Reused, GeneratorDriver Fresh) RunEdit(
        IReadOnlyDictionary<string, string> sources,
        IIncrementalGenerator generator,
        Func<CSharpCompilation, CSharpCompilation> edit,
        bool requireClean)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var original = CSharpCompilation.Create("IncrementalOutputs",
            sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, parseOptions, pair.Key)), GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["INQ039"] = ReportDiagnostic.Warn }));

        GeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        var primed = CreateDriver().RunGeneratorsAndUpdateCompilation(original, out var primedOutput, out var primedDiagnostics);
        Assert.All(primedDiagnostics, static diagnostic => Assert.Equal("INQ039", diagnostic.Id));
        Assert.Empty(primedOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var edited = edit(original);
        var reused = primed.RunGeneratorsAndUpdateCompilation(edited, out var reusedOutput, out var reusedDiagnostics);
        if (requireClean)
        {
            Assert.All(reusedDiagnostics, static diagnostic => Assert.Equal("INQ039", diagnostic.Id));
            Assert.Empty(reusedOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }

        return (reused, CreateDriver().RunGenerators(edited));
    }

    private static CSharpCompilation ReplaceText(CSharpCompilation compilation, string path, string from, string to)
    {
        var tree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
        var text = tree.ToString();
        Assert.Contains(from, text, StringComparison.Ordinal);
        return compilation.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(text.Replace(from, to, StringComparison.Ordinal), (CSharpParseOptions)tree.Options, path));
    }

    /// <summary>The reused driver produces the same sources and diagnostics, in the same order, as a fresh one.</summary>
    private static void AssertSameOutput(GeneratorDriver fresh, GeneratorDriver reused)
    {
        Assert.Equal(SourceSnapshot(fresh), SourceSnapshot(reused));
        var expected = fresh.GetRunResult().Results[0];
        var actual = reused.GetRunResult().Results[0];
        Assert.Equal(expected.GeneratedSources.Select(static source => source.HintName), actual.GeneratedSources.Select(static source => source.HintName));
        Assert.Equal(expected.Diagnostics.Select(DiagnosticSnapshot), actual.Diagnostics.Select(DiagnosticSnapshot));
    }

    // ToString() shows only the line span, so it would accept a stale text span.
    private static string DiagnosticSnapshot(Diagnostic diagnostic)
        => string.Join("|",
            diagnostic.Id,
            diagnostic.Severity,
            diagnostic.WarningLevel,
            diagnostic.IsSuppressed,
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(";", new[] { diagnostic.Location }.Concat(diagnostic.AdditionalLocations).Select(static location =>
                $"{location.Kind}:{location.GetLineSpan().Path}:{location.SourceSpan}:{location.GetLineSpan().Span}")),
            string.Join(";", diagnostic.Properties.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Key + "=" + pair.Value)));

    private static Dictionary<string, string> Reasons(string[] labels, string letters)
    {
        Assert.Equal(labels.Length, letters.Length);
        return labels.Select((label, i) => (label, reason: letters[i] switch { 'C' => C, 'U' => U, _ => M }))
            .ToDictionary(static pair => pair.label, static pair => pair.reason);
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
        RelationSlice slice => slice.Entity.Name,
        StoreSlice slice => slice.Store.Name,
        StoreEmission emission => emission.StoreName,
        StoreFacts facts => facts.StoreName,
        _ => "model",
    };

    private sealed record ExpectedReasons(
        string RelationSlices,
        string RelationOutputs,
        string StoreSlices,
        string StoreEmissions,
        string StoreOutputs,
        string StoreFacts,
        string EntityIndex,
        string ProjectionIndex,
        string StoreOptions,
        string SchemaOptions,
        string Schema);
}
