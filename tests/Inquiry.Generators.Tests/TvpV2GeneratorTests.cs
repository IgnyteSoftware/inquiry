using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerTvpV2CarriesExactStringDecimalBinaryAndTemporalFacets()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VFacetItems")]
            public sealed class FacetItem
            {
                [InquiryColumn(Length = 37, IsUnicode = false)] public string Code { get; set; } = "";
                [InquiryColumn(Precision = 29, Scale = 7)] public decimal Amount { get; set; }
                [InquiryColumn(Length = 17)] public byte[] Payload { get; set; } = Array.Empty<byte>();
                [InquiryColumn(Scale = 3)] public DateTimeOffset CapturedAt { get; set; }
            }
            public partial class FacetStore : InquiryStore<FacetItem>
            {
                [InquiryExists, InquiryWhere("Code", Compare.In)] public partial Task<bool> ByCode(IReadOnlyList<string> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Amount", Compare.In)] public partial Task<bool> ByAmount(IReadOnlyList<decimal> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Payload", Compare.In)] public partial Task<bool> ByPayload(IReadOnlyList<byte[]> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("CapturedAt", Compare.In)] public partial Task<bool> ByTime(IReadOnlyList<DateTimeOffset> values, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Contains("AS TABLE ([Value] VARCHAR(37) NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] DECIMAL(29,7) NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] VARBINARY(17) NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] DATETIMEOFFSET(3) NOT NULL)", generated);
        Assert.Contains("InquiryTvpDescriptor", generated);
    }

    [Fact]
    public void SqlServerNullableCollectionEmitsDistinctNullableArtifactAndRetainsNullRows()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VNullable")]
            public sealed class Item
            {
                [InquiryColumn] public int? NullableValue { get; set; }
                [InquiryColumn] public int Value { get; set; }
            }
            public partial class Store : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("NullableValue", Compare.In)] public partial Task<bool> Nullable(IReadOnlyList<int?> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> NonNullable(IReadOnlyList<int> values, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(generated, "CREATE TYPE").Count);
        Assert.Contains("AS TABLE ([Value] INT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] INT NOT NULL)", generated);
    }

    [Theory]
    [InlineData("[InquiryColumn(SqlType = \"CHAR(36)\")] public System.Guid Value { get; set; }")]
    [InlineData("[InquiryColumn(SqlType = \"INT\")] public string Value { get; set; } = \"\";")]
    [InlineData("[InquiryColumn(SqlType = \"VARCHAR(20)\")] public System.DateTime Value { get; set; }")]
    [InlineData("[InquiryColumn(SqlType = \"NVARCHAR(20)\", Length = 20)] public string Value { get; set; } = \"\";")]
    [InlineData("[InquiryColumn(SqlType = \"XML\")] public string Value { get; set; } = \"\";")]
    public void SqlServerInvalidTvpPhysicalMappingReportsInq076AndEmitsUnreachableStub(string property)
    {
        var source = $$"""
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VInvalid")] public sealed class Item { {{property}} }
            public partial class Store : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Find(IReadOnlyList<{{(property.Contains("Guid") ? "System.Guid" : property.Contains("DateTime") ? "System.DateTime" : "string")}}> values, CancellationToken ct = default); }
            """;

        var result = RunGenerator(
            source,
            dialect: "SqlServer",
            unsupportedOperationSeverity: ReportDiagnostic.Warn);
        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ076" && diagnostic.Severity == DiagnosticSeverity.Error);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.DoesNotContain("CREATE TYPE", generated);
        Assert.DoesNotContain("BindUnsupported", generated);
        Assert.Contains("throw new global::System.NotSupportedException", generated);
    }

    [Fact]
    public void SqlServerNonNullableColumnRejectsNullableCollectionElements()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VItems")] public sealed class Item { [InquiryColumn] public int Value { get; set; } }
            public partial class Store : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Find(IReadOnlyList<int?> values, CancellationToken ct = default); }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ018");
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerOverloadedMethodsKeepCollectionMappingErrorsIsolatedPerOverload()
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("VOverloads")]
            public sealed class Item
            {
                [InquiryColumn] public int Good { get; set; }
                [InquiryColumn(SqlType = "XML")] public string Bad { get; set; } = "";
            }
            public partial class Store : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("Good", Compare.In)]
                public partial Task<bool> Find(IReadOnlyList<int> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Bad", Compare.In)]
                public partial Task<bool> Find(IReadOnlyList<string> values, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(
            source,
            dialect: "SqlServer",
            unsupportedOperationSeverity: ReportDiagnostic.Warn);
        Assert.Single(result.RunResult.Diagnostics.Where(static diagnostic => diagnostic.Id == "INQ076"));
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@Good\", values", store);
        Assert.DoesNotContain("InquiryTvpParameter.Bind(_c, \"@Bad\", values", store);
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(
            store,
            "throw new global::System.NotSupportedException").Cast<global::System.Text.RegularExpressions.Match>());
    }

    [Fact]
    public void SqlServerInferredTemporalFacetsMatchTableAndTvpDdl()
    {
        const string source = """
            using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("TemporalItems")] public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Scale = 0)] public DateTime At { get; set; }
                [InquiryColumn(Scale = 4)] public DateTimeOffset Offset { get; set; }
                [InquiryColumn(Scale = 7)] public TimeOnly Time { get; set; }
            }
            public partial class Store : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("At", Compare.In)] public partial Task<bool> At(IReadOnlyList<DateTime> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Offset", Compare.In)] public partial Task<bool> Offset(IReadOnlyList<DateTimeOffset> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Time", Compare.In)] public partial Task<bool> Time(IReadOnlyList<TimeOnly> values, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains("[At] DATETIME2(0) NOT NULL", generated);
        Assert.Contains("[Offset] DATETIMEOFFSET(4) NOT NULL", generated);
        Assert.Contains("[Time] TIME(7) NOT NULL", generated);
        Assert.Contains("AS TABLE ([Value] DATETIME2(0) NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] DATETIMEOFFSET(4) NOT NULL)", generated);
        Assert.Contains("AS TABLE ([Value] TIME(7) NOT NULL)", generated);
    }

    [Theory]
    [InlineData("[InquiryColumn(SqlType = \"   \")] public int Value { get; set; }")]
    [InlineData("[InquiryColumn(Precision = 0, Scale = 0)] public decimal Value { get; set; }")]
    [InlineData("[InquiryColumn(Scale = 8)] public System.DateTime Value { get; set; }")]
    public void SqlServerInvalidInferredOrWhitespaceFacetsReportOnlyInq076AndDoNotEmitStubByDefault(string property)
    {
        var element = property.Contains("decimal", StringComparison.Ordinal) ? "decimal"
            : property.Contains("DateTime", StringComparison.Ordinal) ? "System.DateTime" : "int";
        var source = $$"""
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("InvalidFacets")] public sealed class Item { {{property}} }
            public partial class Store : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Find(IReadOnlyList<{{element}}> values, CancellationToken ct = default); }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Single(result.RunResult.Diagnostics.Where(static diagnostic => diagnostic.Id == "INQ076"));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ039");
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("throw new global::System.NotSupportedException", store);
        Assert.DoesNotContain("InquiryTvpParameter.Bind", store);
        Assert.Contains(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(ReportDiagnostic.Warn)]
    [InlineData(ReportDiagnostic.Suppress)]
    public void SqlServerInvalidCollectionFacetEmitsCompileSafeStubOnlyWithProjectWideOptIn(
        ReportDiagnostic unsupportedOperationAction)
    {
        const string source = """
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            [InquiryView("InvalidFacets")] public sealed class Item
            { [InquiryColumn(Scale = 8)] public System.DateTime Value { get; set; } }
            public partial class Store : InquiryStore<Item>
            { [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Find(IReadOnlyList<System.DateTime> values, CancellationToken ct = default); }
            """;

        var result = RunGenerator(
            source,
            dialect: "SqlServer",
            unsupportedOperationSeverity: unsupportedOperationAction,
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
            {
                ["INQ076"] = ReportDiagnostic.Suppress,
            });

        Assert.DoesNotContain(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ076");
        Assert.DoesNotContain(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "INQ039");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("throw new global::System.NotSupportedException", store);
        Assert.DoesNotContain("InquiryTvpParameter.Bind", store);
    }

    [Fact]
    public void SqlServerProjectionUsesMethodElementNullabilityAndConvertsUnsignedExactlyOnce()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
            using Inquiry.Entities; using Inquiry.Stores;
            namespace Demo;
            public readonly record struct Token(uint Value);
            public sealed class TokenConverter : IInquiryValueConverter<Token, uint>
            { public uint ToProvider(Token value) => value.Value; public Token FromProvider(uint value) => new(value); }
            [InquiryView("Tokens")] public sealed class Item
            { [InquiryColumn(Converter = typeof(TokenConverter))] public Token? Value { get; set; } }
            public partial class Store : InquiryStore<Item>
            {
                [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> NonNullable(IReadOnlyList<Token> values, CancellationToken ct = default);
                [InquiryExists, InquiryWhere("Value", Compare.In)] public partial Task<bool> Nullable(IReadOnlyList<Token?> values, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var store = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("Store.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("static _e => unchecked((global::System.Int32)(global::Inquiry.Entities.InquiryConverterCache<global::Demo.TokenConverter>.Instance.ToProvider(_e)))", store);
        Assert.Contains("static _e => _e.HasValue ? (global::System.Int32?)unchecked((global::System.Int32)(global::Inquiry.Entities.InquiryConverterCache<global::Demo.TokenConverter>.Instance.ToProvider(_e.Value))) : null", store);
        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(store, "TokenConverter>.Instance.ToProvider\\(").Count);
        Assert.DoesNotContain("_e.HasValue ?", store.Substring(store.IndexOf("NonNullable", StringComparison.Ordinal), store.IndexOf("Nullable", store.IndexOf("NonNullable", StringComparison.Ordinal) + 1, StringComparison.Ordinal) - store.IndexOf("NonNullable", StringComparison.Ordinal)));
    }

    [Fact]
    public void SqlServerGeneratedStoreCachesDescriptorOncePerSignature()
    {
        var source = PredicateSource("""
            [InquiryExists, InquiryWhere("CategoryId", Compare.In)]
            public partial Task<bool> First(IReadOnlyList<int> values, CancellationToken cancellationToken = default);
            [InquiryExists, InquiryWhere("CategoryId", Compare.In)]
            public partial Task<bool> Second(IReadOnlyList<int> values, CancellationToken cancellationToken = default);
            """);
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var store = GeneratedProductStoreText(result);
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(store, "private static readonly global::Inquiry.SqlServer.Parameters.InquiryTvpDescriptor").Cast<global::System.Text.RegularExpressions.Match>());
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(store, "InquiryTvpDescriptor.Get\\(").Cast<global::System.Text.RegularExpressions.Match>());
        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(store, "_inquiryTvpDescriptor_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c\\);").Count);
    }
}
