using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Theory]
    [InlineData("INQ078", "public sealed class BadConverter : IInquiryValueConverter<string, string> { public string ToProvider(string value) => value; public string FromProvider(string value) => value; }")]
    [InlineData("INQ079", "public abstract class BadConverter : IInquiryValueConverter<Model, string> { public abstract string ToProvider(Model value); public abstract Model FromProvider(string value); }")]
    [InlineData("INQ080", "public sealed class BadConverter<T> : IInquiryValueConverter<Model, string> { public string ToProvider(Model value) => string.Empty; public Model FromProvider(string value) => new(); }")]
    [InlineData("INQ082", "public sealed class BadConverter : IInquiryValueConverter<Model, string> { public BadConverter(int value) { } public string ToProvider(Model value) => string.Empty; public Model FromProvider(string value) => new(); }")]
    public void MalformedConverterReportsDedicatedDiagnosticAtTypeofExpression(string expectedId, string converterDeclaration)
    {
        var converterType = expectedId == "INQ080" ? "BadConverter<>" : "BadConverter";
        var source = $$"""
            using Inquiry.Entities;
            namespace Demo;
            public sealed class Model { }
            {{converterDeclaration}}
            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof({{converterType}}))] public Model Value { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        var diagnostics = result.RunResult.Diagnostics.Where(value => value.Id == expectedId).ToArray();
        Assert.Equal(6, diagnostics.Length);
        var expectedText = "typeof(" + converterType + ")";
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(expectedText, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id == "AD0001");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static value => value.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void InaccessibleNestedConverterReportsDedicatedDiagnosticAtTypeofExpression()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            public static class Container
            {
                private sealed class PrivateConverter : IInquiryValueConverter<Model, string>
                {
                    public string ToProvider(Model value) => string.Empty;
                    public Model FromProvider(string value) => new();
                }
                public sealed class Model { }
                [InquiryTable("Item")]
                public sealed class Item
                {
                    [InquiryKey] public int Id { get; set; }
                    [InquiryColumn(Converter = typeof(PrivateConverter))] public Model Value { get; set; } = new();
                }
            }
            """;

        var result = RunGenerator(source);

        var diagnostics = result.RunResult.Diagnostics.Where(static value => value.Id == "INQ081").ToArray();
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal("typeof(PrivateConverter)", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id == "AD0001");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static value => value.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicInternalAndStructConvertersAreAcceptedForNullableAndNonNullableModels()
    {
        const string source = """
            #nullable enable
            using Inquiry.Entities;
            namespace Demo;
            public readonly record struct Token(int Value);
            public sealed class PublicConverter : IInquiryValueConverter<Token, int>
            {
                public int ToProvider(Token value) => value.Value;
                public Token FromProvider(int value) => new(value);
            }
            internal sealed class InternalConverter : IInquiryValueConverter<Token, long>
            {
                public InternalConverter() { }
                public long ToProvider(Token value) => value.Value;
                public Token FromProvider(long value) => new((int)value);
            }
            public readonly struct StructConverter : IInquiryValueConverter<Token, string>
            {
                public string ToProvider(Token value) => value.Value.ToString();
                public Token FromProvider(string value) => new(int.Parse(value));
            }
            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(PublicConverter))] public Token PublicValue { get; set; }
                [InquiryColumn(Converter = typeof(InternalConverter))] public Token? InternalValue { get; set; }
                [InquiryColumn(Converter = typeof(StructConverter))] public Token StructValue { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id is "INQ037" or "INQ038" or "INQ078" or "INQ079" or "INQ080" or "INQ081" or "INQ082");
        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id == "AD0001");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ExplicitInterfaceConverterUsesSelectedContractForReadsScalarWritesAndCollections()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly record struct Token(int Value);
            public readonly struct ExplicitConverter : IInquiryValueConverter<Token, int>
            {
                int IInquiryValueConverter<Token, int>.ToProvider(Token value) => value.Value;
                Token IInquiryValueConverter<Token, int>.FromProvider(int value) => new(value);
            }
            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(ExplicitConverter))] public Token Value { get; set; }
            }
            public partial class ItemStore : InquiryStore<Item>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Item item, CancellationToken cancellationToken = default);

                [InquirySelectAllByPredicate]
                [InquiryWhere("Value", Compare.In)]
                public partial Task<IReadOnlyList<Item>> ByValuesAsync(
                    IReadOnlyList<Token> values,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        const string dispatcher = "global::Inquiry.Entities.InquiryConverterDispatcher<global::Demo.ExplicitConverter, global::Demo.Token, int>";
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains(dispatcher + ".FromProvider(", generated, StringComparison.Ordinal);
        Assert.Contains(dispatcher + ".ToProvider(", generated, StringComparison.Ordinal);
        Assert.Contains("Enumerable.Select(values, static _e => " + dispatcher + ".ToProvider(_e))", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id == "AD0001");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static value => value.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MultipleContractsForExactModelReportConverterInvalidAtTypeofExpression()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            public sealed class Model { }
            public sealed class AmbiguousConverter :
                IInquiryValueConverter<Model, int>,
                IInquiryValueConverter<Model, string>
            {
                int IInquiryValueConverter<Model, int>.ToProvider(Model value) => 0;
                Model IInquiryValueConverter<Model, int>.FromProvider(int value) => new();
                string IInquiryValueConverter<Model, string>.ToProvider(Model value) => string.Empty;
                Model IInquiryValueConverter<Model, string>.FromProvider(string value) => new();
            }
            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(AmbiguousConverter))] public Model Value { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        var diagnostics = result.RunResult.Diagnostics.Where(static value => value.Id == "INQ037").ToArray();
        Assert.Equal(6, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal("typeof(AmbiguousConverter)", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length)));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static value => value.Id == "AD0001");
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(),
            static value => value.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
