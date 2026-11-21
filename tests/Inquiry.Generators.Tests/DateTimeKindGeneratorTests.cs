using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryDateTimeKind]</c> (#613): a declared kind wraps each generated DateTime read in
/// <c>DateTime.SpecifyKind</c>. Without a declaration, the generated code is unchanged.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string DateTimeKindSpecifyUtc = "global::System.DateTime.SpecifyKind(reader.GetDateTime(";

    private const string DateTimeKindSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TEvent")]
        public sealed class Event
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("OccurredAt") /*OccurredAt*/]
            public DateTime OccurredAt { get; set; }

            [InquiryColumn("ClosedAt") /*ClosedAt*/]
            public DateTime? ClosedAt { get; set; }

            [InquiryColumn("Offset")]
            public DateTimeOffset Offset { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        [InquiryProjection(typeof(Event))]
        public sealed class EventSummary
        {
            [InquiryColumn("Id")]
            public long Id { get; set; }

            [InquiryColumn("OccurredAt") /*SummaryOccurredAt*/]
            public DateTime OccurredAt { get; set; }
        }

        [InquiryAdHoc]
        public sealed class EventRow
        {
            /*RowOccurredAt*/
            public DateTime OccurredAt { get; set; }

            public DateTime? ClosedAt { get; set; }
        }

        public partial class EventStore : Inquiry.Stores.InquiryStore<Demo.Event>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Event>> ListAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll]
            public partial Task<IReadOnlyList<EventSummary>> ListSummariesAsync(CancellationToken cancellationToken = default);

            [InquiryGroupCount("OccurredAt")]
            public partial Task<IReadOnlyList<GroupCount<DateTime>>> CountByOccurredAtAsync(CancellationToken cancellationToken = default);
        }
        """;

    private static string GeneratedText(GeneratorTestResult result, string suffix)
        => Assert.Single(result.RunResult.GeneratedTrees, t => t.FilePath.EndsWith(suffix, StringComparison.Ordinal)).GetText().ToString();

    // Assembly attributes must follow the using directives.
    private static string WithAssemblyKind(string source, string kind)
        => source.Replace("namespace Demo;", "[assembly: InquiryDateTimeKind(" + kind + ")]\nnamespace Demo;", StringComparison.Ordinal);

    // Every provider generator reports the same model diagnostics, so compare distinct ones.
    private static Diagnostic[] DistinctDiagnostics(GeneratorTestResult result, string id)
        => result.GeneratorDiagnostics.Where(d => d.Id == id).DistinctBy(static d => (d.Location.SourceSpan, d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))).ToArray();

    private static string AllGeneratedText(GeneratorTestResult result)
        => string.Join("\n", result.RunResult.GeneratedTrees.Select(static t => t.GetText().ToString()));

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("Oracle")]
    public void UndeclaredDateTimeColumnsKeepTheProviderKind(string dialect)
    {
        var result = RunGenerator(DateTimeKindSource, dialect);
        AssertNoErrors(result);

        Assert.DoesNotContain("DateTimeKind", AllGeneratedText(result));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("Oracle")]
    public void UnspecifiedDeclarationsGenerateTheSameCodeAsNoDeclaration(string dialect)
    {
        var baseline = RunGenerator(DateTimeKindSource, dialect);
        AssertNoErrors(baseline);

        // Unspecified is the default, so declaring it at either level must not change any generated file.
        var source = WithAssemblyKind(DateTimeKindSource, "DateTimeKind.Unspecified")
            .Replace("/*OccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Unspecified)", StringComparison.Ordinal);
        var declared = RunGenerator(source, dialect);
        AssertNoErrors(declared);

        var expected = baseline.RunResult.GeneratedTrees.Select(static t => t.GetText().ToString()).ToArray();
        var actual = declared.RunResult.GeneratedTrees.Select(static t => t.GetText().ToString()).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ColumnDeclarationStampsEntityReadsOnly()
    {
        var source = DateTimeKindSource
            .Replace("/*OccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Utc)", StringComparison.Ordinal)
            .Replace("/*ClosedAt*/", ", InquiryDateTimeKind(DateTimeKind.Local)", StringComparison.Ordinal);
        var result = RunGenerator(source);
        AssertNoErrors(result);

        var entity = GeneratedText(result, "Event.InquiryEntity.g.cs");
        // Ordinal materializer, name-mapped in-order path and name-mapped reordered path.
        Assert.Contains("OccurredAt = global::System.DateTime.SpecifyKind(reader.GetDateTime(1), global::System.DateTimeKind.Utc),", entity);
        Assert.Contains("value1 = global::System.DateTime.SpecifyKind(reader.GetDateTime(1), global::System.DateTimeKind.Utc);", entity);
        Assert.Contains("value1 = global::System.DateTime.SpecifyKind(reader.GetDateTime(ordinals[1]), global::System.DateTimeKind.Utc);", entity);
        Assert.Equal(3, Occurrences(entity, "global::System.DateTimeKind.Utc"));

        // The null guard stays outside the stamp.
        Assert.Contains("ClosedAt = reader.IsDBNull(2) ? (global::System.DateTime?)null : global::System.DateTime.SpecifyKind(reader.GetDateTime(2), global::System.DateTimeKind.Local),", entity);
        Assert.Equal(3, Occurrences(entity, "global::System.DateTimeKind.Local"));

        // The projection and the ad-hoc DTO declare nothing, and writes are unchanged.
        Assert.DoesNotContain("DateTimeKind", GeneratedText(result, "EventSummary.InquiryProjection.g.cs"));
        Assert.DoesNotContain("DateTimeKind", GeneratedText(result, "EventRow.InquiryAdHoc.g.cs"));
        var store = GeneratedText(result, "EventStore.InquiryStore.g.cs");
        Assert.Contains("new global::Inquiry.GroupCount<global::System.DateTime>(global::System.DateTime.SpecifyKind(reader.GetDateTime(0), global::System.DateTimeKind.Utc), ", store);
        Assert.Equal(1, Occurrences(store, "SpecifyKind"));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void AssemblyDefaultStampsEveryDateTimeRead(string dialect)
    {
        var source = WithAssemblyKind(DateTimeKindSource, "DateTimeKind.Utc");
        var result = RunGenerator(source, dialect);
        AssertNoErrors(result);

        var entity = GeneratedText(result, "Event.InquiryEntity.g.cs");
        Assert.Equal(6, Occurrences(entity, "global::System.DateTimeKind.Utc"));
        Assert.DoesNotContain("SpecifyKind(reader.GetFieldValue<global::System.DateTimeOffset>", entity);

        var projection = GeneratedText(result, "EventSummary.InquiryProjection.g.cs");
        Assert.Equal(3, Occurrences(projection, DateTimeKindSpecifyUtc));

        var adHoc = GeneratedText(result, "EventRow.InquiryAdHoc.g.cs");
        Assert.Equal(4, Occurrences(adHoc, DateTimeKindSpecifyUtc));
        Assert.Contains("reader.IsDBNull(1) ? (global::System.DateTime?)null : global::System.DateTime.SpecifyKind(reader.GetDateTime(1), global::System.DateTimeKind.Utc)", adHoc);

        var store = GeneratedText(result, "EventStore.InquiryStore.g.cs");
        Assert.Contains("new global::Inquiry.GroupCount<global::System.DateTime>(global::System.DateTime.SpecifyKind(reader.GetDateTime(0), global::System.DateTimeKind.Utc), ", store);
    }

    [Fact]
    public void PropertyDeclarationOverridesTheAssemblyDefault()
    {
        var source = WithAssemblyKind(DateTimeKindSource, "DateTimeKind.Utc")
            .Replace("/*OccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Unspecified)", StringComparison.Ordinal)
            .Replace("/*SummaryOccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Local)", StringComparison.Ordinal)
            .Replace("/*RowOccurredAt*/", "[InquiryDateTimeKind(DateTimeKind.Unspecified)]", StringComparison.Ordinal);
        var result = RunGenerator(source);
        AssertNoErrors(result);

        var entity = GeneratedText(result, "Event.InquiryEntity.g.cs");
        Assert.Contains("OccurredAt = reader.GetDateTime(1),", entity);
        Assert.Equal(3, Occurrences(entity, "global::System.DateTimeKind.Utc"));

        var projection = GeneratedText(result, "EventSummary.InquiryProjection.g.cs");
        Assert.Equal(3, Occurrences(projection, "global::System.DateTimeKind.Local"));
        Assert.DoesNotContain("DateTimeKind.Utc", projection);

        var adHoc = GeneratedText(result, "EventRow.InquiryAdHoc.g.cs");
        Assert.Contains("value0 = reader.GetDateTime(0);", adHoc);
        Assert.Equal(2, Occurrences(adHoc, DateTimeKindSpecifyUtc));
    }

    private const string DateTimeConverterSource = """
        using System;
        using Inquiry.Entities;

        namespace Demo;

        public readonly record struct Instant(DateTime Utc);

        public sealed class InstantConverter : IInquiryValueConverter<Instant, DateTime>
        {
            public DateTime ToProvider(Instant value) => value.Utc;
            public Instant FromProvider(DateTime value) => new(value);
        }

        public sealed class TicksConverter : IInquiryValueConverter<DateTime, long>
        {
            public long ToProvider(DateTime value) => value.Ticks;
            public DateTime FromProvider(long value) => new(value);
        }

        [InquiryTable("TStamp")]
        public sealed class Stamp
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("At", Converter = typeof(InstantConverter)) /*At*/]
            public Instant At { get; set; }

            [InquiryColumn("Ticks", Converter = typeof(TicksConverter)) /*Ticks*/]
            public DateTime Ticks { get; set; }
        }
        """;

    [Fact]
    public void ConverterWithDateTimeProviderReceivesTheStampedValue()
    {
        var result = RunGenerator(DateTimeConverterSource.Replace("/*At*/", ", InquiryDateTimeKind(DateTimeKind.Utc)", StringComparison.Ordinal));
        AssertNoErrors(result);

        var entity = GeneratedText(result, "Stamp.InquiryEntity.g.cs");
        Assert.Contains(".FromProvider(global::System.DateTime.SpecifyKind(reader.GetDateTime(1), global::System.DateTimeKind.Utc))", entity);
        // The long-provider converter reads no DateTime, so it is not stamped.
        Assert.DoesNotContain("SpecifyKind(reader.GetInt64", entity);
    }

    [Fact]
    public void AssemblyDefaultSkipsConvertersWithNonDateTimeProviders()
    {
        var result = RunGenerator(WithAssemblyKind(DateTimeConverterSource, "DateTimeKind.Utc"));
        AssertNoErrors(result);

        var entity = GeneratedText(result, "Stamp.InquiryEntity.g.cs");
        Assert.Equal(3, Occurrences(entity, "SpecifyKind(reader.GetDateTime("));
        Assert.DoesNotContain("SpecifyKind(reader.GetInt64", entity);
    }

    [Theory]
    [InlineData("Ticks", "InquiryDateTimeKind(DateTimeKind.Utc)", "'long'")]
    [InlineData("At", "InquiryDateTimeKind((DateTimeKind)7)", "7 is not a System.DateTimeKind value")]
    public void InvalidColumnDeclarationReportsINQ119(string property, string attribute, string reason)
    {
        var source = DateTimeConverterSource.Replace("/*" + property + "*/", ", " + attribute, StringComparison.Ordinal);
        var result = RunGenerator(source);

        var diagnostic = Assert.Single(DistinctDiagnostics(result, "INQ119"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Stamp." + property + "'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        // The diagnostic points at the attribute.
        var lines = source.Split('\n');
        var start = diagnostic.Location.GetLineSpan().StartLinePosition;
        Assert.StartsWith(attribute, lines[start.Line][start.Character..], StringComparison.Ordinal);
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Severity == DiagnosticSeverity.Error && d.Id != "INQ119");
    }

    [Theory]
    [InlineData("Name", "'string'")]
    [InlineData("Offset", "'System.DateTimeOffset'")]
    public void DeclarationOnANonDateTimeColumnReportsINQ119(string column, string reason)
    {
        var source = DateTimeKindSource.Replace(
            "[InquiryColumn(\"" + column + "\")]",
            "[InquiryColumn(\"" + column + "\"), InquiryDateTimeKind(DateTimeKind.Utc)]",
            StringComparison.Ordinal);
        var result = RunGenerator(source);

        var diagnostic = Assert.Single(DistinctDiagnostics(result, "INQ119"));
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void UndefinedAssemblyDefaultReportsINQ119AtEachDateTimeColumn()
    {
        var result = RunGenerator(WithAssemblyKind(DateTimeKindSource, "(DateTimeKind)9"));

        var diagnostics = DistinctDiagnostics(result, "INQ119");
        // Event.OccurredAt, Event.ClosedAt, EventSummary.OccurredAt, EventRow.OccurredAt and EventRow.ClosedAt.
        Assert.Equal(5, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains("9 is not a System.DateTimeKind value", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DateTimeAggregatesApplyTheColumnKind()
    {
        var source = DateTimeKindSource
            .Replace("/*OccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Utc)", StringComparison.Ordinal)
            .Replace("[InquiryGroupCount(", """
                [InquiryAggregate(InquiryAggregateFunction.Max, "OccurredAt")]
                public partial Task<DateTime?> LatestAsync(CancellationToken cancellationToken = default);

                [InquiryAggregate(InquiryAggregateFunction.Min, "OccurredAt")]
                [InquiryWhere("Name")]
                public partial Task<DateTime> EarliestForAsync(string name, CancellationToken cancellationToken = default);

                [InquiryAggregate(InquiryAggregateFunction.Max, "ClosedAt")]
                public partial Task<DateTime?> LastClosedAsync(CancellationToken cancellationToken = default);

                [InquiryGroupCount(
                """, StringComparison.Ordinal);
        var result = RunGenerator(source);
        AssertNoErrors(result);

        var store = GeneratedText(result, "EventStore.InquiryStore.g.cs");
        Assert.Contains("public async partial global::System.Threading.Tasks.Task<global::System.DateTime?> LatestAsync(", store);
        Assert.Contains("return _value is { } _set ? global::System.DateTime.SpecifyKind(_set, global::System.DateTimeKind.Utc) : null;", store);
        Assert.Contains("public async partial global::System.Threading.Tasks.Task<global::System.DateTime> EarliestForAsync(", store);
        Assert.Contains("return global::System.DateTime.SpecifyKind(_value, global::System.DateTimeKind.Utc);", store);
        // ClosedAt declares no kind, so its aggregate keeps the direct scalar return.
        Assert.Contains("public partial global::System.Threading.Tasks.Task<global::System.DateTime?> LastClosedAsync(", store);
    }

    [Fact]
    public void DateTimeAggregateLocalsAvoidParameterNames()
    {
        var source = DateTimeKindSource
            .Replace("/*OccurredAt*/", ", InquiryDateTimeKind(DateTimeKind.Utc)", StringComparison.Ordinal)
            .Replace("[InquiryGroupCount(", """
                [InquiryAggregate(InquiryAggregateFunction.Max, "OccurredAt")]
                public partial Task<DateTime?> LatestAsync(CancellationToken _value = default);

                [InquiryAggregate(InquiryAggregateFunction.Max, "OccurredAt")]
                [InquiryWhere("Name")]
                public partial Task<DateTime?> LatestForAsync(string _set, CancellationToken __value = default);

                [InquiryGroupCount(
                """, StringComparison.Ordinal);

        // AssertNoErrors compiles the generated code, so a local that redeclares a parameter fails here.
        var result = RunGenerator(source);
        AssertNoErrors(result);

        var store = GeneratedText(result, "EventStore.InquiryStore.g.cs");
        Assert.Contains("var __value = await ", store);
        Assert.Contains("return _value is { } __set ?", store);
    }

    [Fact]
    public void ChangingTheAssemblyDefaultInAnotherFileRegeneratesMaterializers()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp10);
        var entityTree = CSharpSyntaxTree.ParseText(DateTimeKindSource, parseOptions);
        var dialectTree = CSharpSyntaxTree.ParseText("[assembly: global::Inquiry.InquiryDialect(\"Sqlite\")]", parseOptions);
        var kindV1 = CSharpSyntaxTree.ParseText("// no default", parseOptions);
        var compilation = CSharpCompilation.Create(
            "InquiryDateTimeKindIncrementalTests",
            new[] { entityTree, dialectTree, kindV1 },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator() },
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        string EntityText() => driver.GetRunResult().Results[0].GeneratedSources
            .Single(static s => s.HintName.EndsWith("Event.InquiryEntity.g.cs", StringComparison.Ordinal)).SourceText.ToString();

        driver = driver.RunGenerators(compilation);
        Assert.DoesNotContain("DateTimeKind", EntityText());

        // Only the file that holds the assembly attribute changes; the entity syntax is untouched.
        var kindV2 = CSharpSyntaxTree.ParseText("[assembly: Inquiry.Entities.InquiryDateTimeKind(System.DateTimeKind.Utc)]", parseOptions);
        compilation = compilation.ReplaceSyntaxTree(kindV1, kindV2);
        driver = driver.RunGenerators(compilation);
        Assert.Equal(6, Occurrences(EntityText(), "global::System.DateTimeKind.Utc"));

        var kindV3 = CSharpSyntaxTree.ParseText("// no default", parseOptions);
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(kindV2, kindV3));
        Assert.DoesNotContain("DateTimeKind", EntityText());
    }

    [Fact]
    public void EagerJunctionKeyReadsApplyTheColumnKind()
    {
        var source = WithAssemblyKind(
            OrderProductSource.Replace("using System.Collections.Generic;", "using System;\nusing System.Collections.Generic;", StringComparison.Ordinal)
                .Replace("public long ", "public DateTime ", StringComparison.Ordinal)
                .Replace("(long id,", "(DateTime id,", StringComparison.Ordinal),
            "DateTimeKind.Utc");
        Assert.DoesNotContain("long ", source, StringComparison.Ordinal);
        var result = RunGenerator(source);
        AssertNoErrors(result);

        var store = GetOrderStore(result);
        Assert.Contains("_jParentKey = global::System.DateTime.SpecifyKind(reader.GetDateTime(0), global::System.DateTimeKind.Utc);", store);
        Assert.Contains("_jChildKey = global::System.DateTime.SpecifyKind(reader.GetDateTime(1), global::System.DateTimeKind.Utc);", store);
    }
}
