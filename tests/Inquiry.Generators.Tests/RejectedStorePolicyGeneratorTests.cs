using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

/// <summary>
/// The rejected-store policy (#268, option 2): only an INQ039 method can become a throwing stub, and only
/// with the project-wide INQ039 opt-in. Every other rejected method has no generated member, the store
/// constructor is still emitted, and the build fails with CS8795 whatever the rejecting error's severity.
/// Each rejection path runs with the rejecting error at its default severity, suppressed, and suppressed
/// with INQ039 lowered (#271).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    public enum RejectionSeverity
    {
        Default,
        Suppressed,
        SuppressedWithInq039Lowered,
    }

    private const string RejectedItemEntity = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TItem")]
        public sealed class Item
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }

        """;

    private static string RejectedItemStore(string methods, string attributes = "") =>
        RejectedItemEntity.Replace("\r\n", "\n", StringComparison.Ordinal) +
        attributes + "public partial class ItemStore : InquiryStore<Item>\n{\n" + methods + "\n}\n";

    private const string UnmappedEntityStore = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public sealed class Item
        {
            public long Id { get; set; }
        }

        public partial class ItemStore : InquiryStore<Item>
        {
            [InquirySelectAll]
            public partial IAsyncEnumerable<Item> AllAsync(CancellationToken cancellationToken = default);
        }
        """;

    private const string DatabaseGeneratedTokenStore = """
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TDoc")]
        public sealed class Doc
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("RowVer"), InquiryConcurrencyToken(DatabaseGenerated = true)]
            public byte[] RowVer { get; set; } = System.Array.Empty<byte>();
        }

        public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
        {
            [InquiryUpdate]
            public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
        }
        """;

    private const string InvalidCollectionFacetStore = """
        using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
        using Inquiry.Entities; using Inquiry.Stores;
        namespace Demo;
        [InquiryView("InvalidFacets")] public sealed class Item
        { [InquiryColumn(Scale = 8)] public System.DateTime Value { get; set; } }
        public partial class ItemStore : InquiryStore<Item>
        { [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Find(IReadOnlyList<System.DateTime> values, CancellationToken ct = default); }
        """;

    /// <summary>One rejection path from the #268 policy table.</summary>
    /// <param name="Source">Consumer source.</param>
    /// <param name="Dialect">Active dialect.</param>
    /// <param name="Store">Simple name of the store class.</param>
    /// <param name="RejectingIds">The Inquiry errors that reject the declaration.</param>
    /// <param name="ConstructorOnly">True when the generated store holds only the constructor.</param>
    /// <param name="Present">Members the generated store must contain.</param>
    /// <param name="Absent">Members the generated store must not contain.</param>
    public sealed record RejectionPath(
        string Source,
        string Dialect,
        string Store,
        string[] RejectingIds,
        bool ConstructorOnly,
        string[] Present,
        string[] Absent);

    private static readonly Dictionary<string, RejectionPath> _rejectionPaths = new(StringComparer.Ordinal)
    {
        ["ProviderFacet"] = new(InvalidCollectionFacetStore, "SqlServer", "ItemStore", ["INQ076"],
            ConstructorOnly: false, Present: [], Absent: ["Find(", "NotSupportedException"]),
        ["Mixed"] = new(TenantDocStore("""
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
                """), "Sqlite", "DocStore", ["INQ095"],
            ConstructorOnly: false, Present: ["UpdateAsync("], Absent: ["UpsertAsync(", "NotSupportedException"]),
        ["AllRejectedWriteFilter"] = new(TenantDocStore("""
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);
                """), "Sqlite", "DocStore", ["INQ095"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["AllRejectedUnknownField"] = new(RejectedItemStore("""
                [InquirySelectAllByField("Missing")]
                public partial IAsyncEnumerable<Item> FindMissingAsync(string missing, CancellationToken cancellationToken = default);
                """), "Sqlite", "ItemStore", ["INQ007"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["InvalidEntity"] = new(UnmappedEntityStore, "Sqlite", "ItemStore", ["INQ008"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["ExtractionRejectedNoCriteria"] = new(RejectedItemStore("""
                [InquirySelectAllByPredicate]
                public partial Task<IReadOnlyList<Item>> FindAsync(CancellationToken cancellationToken = default);
                """), "Sqlite", "ItemStore", ["INQ019"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["ExtractionRejectedReturnType"] = new(RejectedItemStore("""
                [InquirySelectAll]
                public partial Task<List<Item>> AllAsync(CancellationToken cancellationToken = default);
                """), "Sqlite", "ItemStore", ["INQ005"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["UnsupportedSignature"] = new(RejectedItemStore("""
                [InquiryCount]
                public static partial Task<long> CountAsync(CancellationToken cancellationToken = default);
                """), "Sqlite", "ItemStore", ["INQ111"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["DatabaseGeneratedToken"] = new(DatabaseGeneratedTokenStore, "Sqlite", "DocStore", ["INQ068", "INQ113"],
            ConstructorOnly: true, Present: [], Absent: []),
        ["TokenUpsert"] = new(TokenStore("""
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Widget widget, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);
                """), "Sqlite", "WidgetStore", ["INQ113"],
            ConstructorOnly: true, Present: [], Absent: []),
    };

    public static TheoryData<string, RejectionSeverity> RejectionPathCases()
    {
        var data = new TheoryData<string, RejectionSeverity>();
        foreach (var path in _rejectionPaths.Keys)
        {
            foreach (var severity in Enum.GetValues<RejectionSeverity>())
            {
                data.Add(path, severity);
            }
        }

        return data;
    }

    private static GeneratorTestResult RunWithRejectionSeverity(
        string source, string dialect, IEnumerable<string> rejectingIds, RejectionSeverity severity)
    {
        var options = severity == RejectionSeverity.Default
            ? null
            : rejectingIds.ToDictionary(static id => id, static _ => ReportDiagnostic.Suppress);
        return RunGenerator(
            source,
            dialect: dialect,
            additionalDiagnosticOptions: options,
            unsupportedOperationSeverity: severity == RejectionSeverity.SuppressedWithInq039Lowered ? ReportDiagnostic.Warn : null);
    }

    private static string ConstructorOnlyStore(string store, string? generatedInterface = null)
    {
        var header = generatedInterface is null ? $"partial class {store}" : $"partial class {store} : {generatedInterface}";
        var text = "// <auto-generated />\n#nullable enable\nnamespace Demo\n{\n" + header + "\n{\n" +
            $"    public {store}(global::Inquiry.IInquiry inquiry)\n        : base(inquiry)\n    {{\n    }}\n}}\n";
        if (generatedInterface is not null)
        {
            text += "\n/// <summary>\n" +
                $"/// Generated interface for <see cref=\"{store}\"/>. Registered in DI as a scoped\n" +
                "/// forward to the concrete store, so services can depend on (and mock) the interface.\n" +
                "/// </summary>\n" +
                $"public partial interface {generatedInterface}\n{{\n}}\n";
        }

        return text + "}\n";
    }

    private static int LineOf(string source, string text)
        => Array.FindIndex(source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), line => line.Contains(text, StringComparison.Ordinal));

    private static string? GeneratedFile(GeneratorTestResult result, string fileName)
        => result.RunResult.GeneratedTrees
            .SingleOrDefault(tree => tree.FilePath.EndsWith(fileName, StringComparison.Ordinal))
            ?.GetText().ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(RejectionPathCases))]
    public void RejectedStorePathNeverProducesARunnableMethod(string pathName, RejectionSeverity severity)
    {
        var path = _rejectionPaths[pathName];
        var result = RunWithRejectionSeverity(path.Source, path.Dialect, path.RejectingIds, severity);

        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
        foreach (var id in path.RejectingIds)
        {
            if (severity == RejectionSeverity.Default)
            {
                Assert.Contains(result.RunResult.Diagnostics, d => d.Id == id && d.Severity == DiagnosticSeverity.Error);
            }
            else
            {
                Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == id);
            }
        }

        // Lowering INQ039 never stubs a method that did not report INQ039.
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ039");

        var store = GeneratedFile(result, path.Store + ".InquiryStore.g.cs");
        Assert.NotNull(store);
        Assert.DoesNotContain("NotSupportedException", store);
        if (path.ConstructorOnly)
        {
            Assert.Equal(ConstructorOnlyStore(path.Store), store);
        }

        foreach (var member in path.Present)
        {
            Assert.Contains(member, store);
        }

        foreach (var member in path.Absent)
        {
            Assert.DoesNotContain(member, store);
        }

        // The store has generated source, so it is registered. It cannot compile, so the registration
        // never runs: every remaining compiler error is the missing implementation of a rejected method.
        var registration = GeneratedFile(result, "InquiryGeneratedServiceRegistration.g.cs");
        Assert.NotNull(registration);
        Assert.Contains($"TryAddScoped<global::Demo.{path.Store}>(services);", registration);
        var errors = CompileErrors(result);
        Assert.NotEmpty(errors);
        Assert.All(errors, static d => Assert.Equal("CS8795", d.Id));
    }

    [Theory]
    [InlineData(RejectionSeverity.Default)]
    [InlineData(RejectionSeverity.Suppressed)]
    [InlineData(RejectionSeverity.SuppressedWithInq039Lowered)]
    public void Inq039IsTheOnlyRejectionThatCanStubAMethod(RejectionSeverity severity)
    {
        // For INQ039 the rejecting error is the opt-in itself: suppressing or lowering it project-wide
        // produces the stub. Both configurations build today, and still do.
        var result = RunGenerator(
            DeleteReturningSource,
            dialect: "Sqlite",
            unsupportedOperationSeverity: severity switch
            {
                RejectionSeverity.Suppressed => ReportDiagnostic.Suppress,
                RejectionSeverity.SuppressedWithInq039Lowered => ReportDiagnostic.Warn,
                _ => null,
            });

        var store = GeneratedFile(result, "WidgetStore.InquiryStore.g.cs");
        Assert.NotNull(store);
        Assert.Contains("TryAddScoped<global::Demo.WidgetStore>(services);", GeneratedFile(result, "InquiryGeneratedServiceRegistration.g.cs"));
        if (severity == RejectionSeverity.Default)
        {
            Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ039" && d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain("DeleteReturningAsync(", store);
            Assert.All(CompileErrors(result), static d => Assert.Equal("CS8795", d.Id));
            Assert.NotEmpty(CompileErrors(result));
        }
        else
        {
            Assert.Contains("public partial global::System.Threading.Tasks.Task<global::Demo.Widget?> DeleteReturningAsync(long id, global::System.Threading.CancellationToken cancellationToken)", store);
            Assert.Contains("throw new global::System.NotSupportedException", store);
            Assert.Empty(CompileErrors(result));
        }
    }

    [Fact]
    public void ConstructorOnlyStoreKeepsAnEmptyGeneratedInterface()
    {
        var result = RunGenerator(RejectedItemStore("""
                [InquirySelectAllByPredicate]
                public partial Task<IReadOnlyList<Item>> FindAsync(CancellationToken cancellationToken = default);
                """, attributes: "[InquiryGenerateInterface]\n"));

        Assert.Equal(ConstructorOnlyStore("ItemStore", "IItemStore"), GeneratedFile(result, "ItemStore.InquiryStore.g.cs"));
        var registration = GeneratedFile(result, "InquiryGeneratedServiceRegistration.g.cs");
        Assert.Contains("TryAddScoped<global::Demo.ItemStore>(services);", registration);
        Assert.Contains("TryAddScoped<global::Demo.IItemStore>(services, static sp =>", registration);
        var errors = CompileErrors(result);
        Assert.NotEmpty(errors);
        Assert.All(errors, static d => Assert.Equal("CS8795", d.Id));
    }

    [Fact]
    public void GeneratedInterfaceOmitsMethodsThatHaveNoGeneratedImplementation()
    {
        var result = RunGenerator(
            InvalidCollectionFacetStore
                .Replace("public partial class ItemStore", "[InquiryGenerateInterface] public partial class ItemStore", StringComparison.Ordinal)
                .Replace("ct = default); }", "ct = default);\n[InquiryCount] public partial Task<long> CountAsync(CancellationToken ct = default); }", StringComparison.Ordinal),
            dialect: "SqlServer");

        var store = GeneratedFile(result, "ItemStore.InquiryStore.g.cs");
        Assert.NotNull(store);
        var generatedInterface = store[store.IndexOf("public partial interface IItemStore", StringComparison.Ordinal)..];
        Assert.Contains("CountAsync(", generatedInterface);
        Assert.DoesNotContain("Find(", generatedInterface);
        var errors = CompileErrors(result);
        Assert.NotEmpty(errors);
        Assert.All(errors, static d => Assert.Equal("CS8795", d.Id));
    }

    [Theory]
    [InlineData("[InquiryCount]\n    public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);", "INQ010")]
    [InlineData("[InquiryCount]\n    partial void Count(CancellationToken cancellationToken);", "INQ005")]
    public void RejectedDeclarationThatNeedsNoGeneratedBodyDoesNotGainAConstructor(string method, string rejectingId)
    {
        // These declarations need no generated implementation, so the only error left after the Inquiry
        // error is suppressed is the missing constructor. Generating one would make the build succeed.
        var result = RunGenerator(
            RejectedItemStore(method),
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { [rejectingId] = ReportDiagnostic.Suppress });

        Assert.Null(GeneratedFile(result, "ItemStore.InquiryStore.g.cs"));
        Assert.Contains(CompileErrors(result), static d => d.Id == "CS7036");
    }

    [Fact]
    public void TokenStoreWhoseMethodsAreHandWrittenDoesNotGainAConstructor()
    {
        // Every method already has a hand-written body, so a generated constructor would let the
        // suppressed configuration build. The store keeps no generated source, as before.
        var source = DatabaseGeneratedTokenStore.Replace(
            "public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);",
            "public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);\n    public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken) => Task.FromResult(true);",
            StringComparison.Ordinal);
        var result = RunGenerator(
            source,
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                ["INQ068"] = ReportDiagnostic.Suppress,
                ["INQ113"] = ReportDiagnostic.Suppress,
            });

        Assert.Null(GeneratedFile(result, "DocStore.InquiryStore.g.cs"));
        Assert.Contains(CompileErrors(result), static d => d.Id == "CS7036");
    }

    public static TheoryData<string, string, LanguageVersion> UnsupportedSignatureCases() => new()
    {
        { "[InquiryCount]\n    public static partial Task<long> CountAsync(CancellationToken cancellationToken = default);", "the method is static", LanguageVersion.CSharp10 },
        { "[InquiryCount]\n    public partial Task<long> CountAsync<T>(CancellationToken cancellationToken = default) where T : struct;", "the method is generic", LanguageVersion.CSharp10 },
        { "[InquirySelectOneByKey]\n    public partial Task<Item?> GetAsync(ref long id, CancellationToken cancellationToken = default);", "parameter 'id' is 'ref'", LanguageVersion.CSharp10 },
        { "[InquirySelectOneByKey]\n    public partial Task<Item?> GetAsync(in long id, CancellationToken cancellationToken = default);", "parameter 'id' is 'in'", LanguageVersion.CSharp10 },
        { "[InquiryCount]\n    public partial Task<long> CountAsync(out int count, CancellationToken cancellationToken = default);", "parameter 'count' is 'out'", LanguageVersion.CSharp10 },
        { "[InquirySelectOneByKey]\n    public partial Task<Item?> GetAsync(ref readonly long id, CancellationToken cancellationToken = default);", "parameter 'id' is 'ref readonly'", LanguageVersion.CSharp12 },
        { "[InquiryStoredProcedure(\"dbo.Touch\")]\n    public partial Task<int> TouchAsync(scoped System.ReadOnlySpan<byte> data, CancellationToken cancellationToken = default);", "parameter 'data' is 'scoped'", LanguageVersion.CSharp11 },
        { "[InquiryCount]\n    public partial ref Task<long> CountAsync(CancellationToken cancellationToken = default);", "the method returns by reference", LanguageVersion.CSharp10 },
        { "[InquiryStoredProcedure(\"dbo.Touch\")]\n    public partial Task<int> TouchAsync(out int rows, CancellationToken cancellationToken = default);", "parameter 'rows' is 'out'", LanguageVersion.CSharp10 },
        { "[InquiryCount]\n    public static partial Task<long> CountAsync<T>(ref int seed, CancellationToken cancellationToken = default);", "the method is static; the method is generic; parameter 'seed' is 'ref'", LanguageVersion.CSharp10 },
    };

    [Theory]
    [MemberData(nameof(UnsupportedSignatureCases))]
    public void UnsupportedSignatureReportsInq111AndEmitsNoMember(string method, string reason, LanguageVersion languageVersion)
    {
        foreach (var lowered in new[] { false, true })
        {
            var result = RunGenerator(
                RejectedItemStore(method),
                dialect: "SqlServer",
                unsupportedOperationSeverity: lowered ? ReportDiagnostic.Warn : null,
                languageVersion: languageVersion);

            var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ111");
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains($"has an unsupported signature: {reason}.", diagnostic.GetMessage(CultureInfo.InvariantCulture));
            Assert.NotEqual(Location.None, diagnostic.Location);
            Assert.Single(result.RunResult.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

            Assert.Equal(ConstructorOnlyStore("ItemStore"), GeneratedFile(result, "ItemStore.InquiryStore.g.cs"));
            var errors = CompileErrors(result);
            Assert.NotEmpty(errors);
            Assert.All(errors, static d => Assert.Equal("CS8795", d.Id));
        }
    }

    [Fact]
    public void ByValueInputOutputProcedureParameterIsNotAnUnsupportedSignature()
    {
        var result = RunGenerator(
            RejectedItemStore("""
                [InquiryStoredProcedure("dbo.Bump")]
                public partial Task<int> BumpAsync([InquiryParameter(IsInputOutput = true)] int counter, CancellationToken cancellationToken = default);
                """),
            dialect: "SqlServer");

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ111");
    }

    [Fact]
    public void TokenUpsertReportsInq113AtTheUpsertMethod()
    {
        var source = TokenStore("""
            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Widget widget, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            public partial Task<bool> UpdateAsync(Widget widget, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ113");
        Assert.Equal(
            "Store 'WidgetStore' cannot generate method 'UpsertAsync': upsert is not supported for entity 'Widget' because it declares concurrency token 'Version'. Use separate insert and update methods.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(LineOf(source, "UpsertAsync("), diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ006");
    }

    [Fact]
    public void UnsupportedDatabaseGeneratedTokenReportsInq113AtTheStore()
    {
        var result = RunGenerator(DatabaseGeneratedTokenStore);

        Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ068");
        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ113");
        Assert.Equal(
            "Store 'DocStore' cannot generate method 'UpdateAsync': the 'Sqlite' dialect does not support database-generated concurrency token 'Doc.RowVer'. Use an application-managed token, or compile against a dialect that supports database-generated tokens.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(LineOf(DatabaseGeneratedTokenStore, "class DocStore"), diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void PragmaAroundTheDeclarationDoesNotSuppressTheGeneratorDiagnostic()
    {
        var result = RunGenerator(RejectedItemStore("""
                #pragma warning disable INQ019
                [InquirySelectAllByPredicate]
                public partial Task<IReadOnlyList<Item>> FindAsync(CancellationToken cancellationToken = default);
                #pragma warning restore INQ019
                """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ019" && d.Severity == DiagnosticSeverity.Error);
    }
}
