using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryJson]</c> with a source-generated <c>JsonSerializerContext</c>: the assembly default and the
/// per-property override select <c>InquiryJsonContextConverter</c>, and missing metadata fails the build.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    // The test compilation does not run the System.Text.Json generator, so each context declares the
    // members that generator would emit.
    private static string JsonContextSource(string assemblyContext, string propertyAttribute, string contexts) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using System.Text.Json.Serialization.Metadata;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        {{assemblyContext}}

        namespace Demo;

        public sealed class Prefs { public string Theme { get; set; } = ""; }

        {{contexts}}

        [InquiryTable("Customer")]
        public sealed class Customer
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn, {{propertyAttribute}}] public Prefs? Prefs { get; set; }
        }

        public partial class CustomerStore : InquiryStore<Customer>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Customer customer, CancellationToken cancellationToken = default);
        }
        """;

    private static string JsonContext(string name, string attributes) => $$"""
        {{attributes}}
        internal sealed class {{name}} : JsonSerializerContext
        {
            public {{name}}() : base(null) { }
            public static {{name}} Default { get; } = new();
            protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
            public override JsonTypeInfo? GetTypeInfo(Type type) => null;
        }
        """;

    [Theory]
    [InlineData("[assembly: InquiryJsonContext(typeof(Demo.AppContext))]", "InquiryJson", "AppContext")]
    [InlineData("[assembly: InquiryJsonContext(typeof(Demo.AppContext))]", "InquiryJson(Context = typeof(OtherContext))", "OtherContext")]
    [InlineData("", "InquiryJson(Context = typeof(OtherContext))", "OtherContext")]
    public void JsonContextSelectsMetadataConverter(string assemblyContext, string propertyAttribute, string expectedContext)
    {
        var source = JsonContextSource(
            assemblyContext,
            propertyAttribute,
            JsonContext("AppContext", "[JsonSerializable(typeof(Prefs))]") +
            JsonContext("OtherContext", "[JsonSerializable(typeof(Prefs))]"));

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var context = "global::Demo." + expectedContext;
        var converter = "global::Inquiry.Converters.InquiryJsonContextConverter<" + context + ", global::Demo.Prefs>";
        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Customer.InquiryEntity.g.cs", StringComparison.Ordinal));
        Assert.Contains(converter + ".FromProvider(reader.GetString(1), " + context + ".Default)", entity.GetText().ToString());
        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        Assert.Contains(converter + ".ToProvider(", store.GetText().ToString());
        Assert.Contains(", " + context + ".Default)", store.GetText().ToString());
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.GetText().ToString().Contains("InquiryJsonConverter<", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[JsonSerializable(typeof(List<Prefs>))]")]
    [InlineData("[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]\n[JsonSerializable(typeof(Prefs))]")]
    [InlineData("[JsonSerializable(typeof(Prefs), GenerationMode = JsonSourceGenerationMode.Serialization)]")]
    public void JsonContextWithoutDeserializationMetadataReportsInq110(string contextAttributes)
    {
        var source = JsonContextSource(
            "[assembly: InquiryJsonContext(typeof(Demo.AppContext))]",
            "InquiryJson",
            JsonContext("AppContext", contextAttributes));

        var result = RunGenerator(source);

        var diagnostic = result.RunResult.Diagnostics.First(static value => value.Id == "INQ110");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("[JsonSerializable(typeof(Prefs))]", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.DoesNotContain(result.RunResult.GeneratedTrees, static t => t.GetText().ToString().Contains("InquiryJsonConverter<", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("typeof(Prefs)")]
    [InlineData("null")]
    [InlineData("typeof(NoDefaultContext)")]
    public void InvalidJsonContextReportsInq108AtContextArgument(string contextArgument)
    {
        var source = JsonContextSource(
            "[assembly: InquiryJsonContext(typeof(Demo.AppContext))]",
            "InquiryJson(Context = " + contextArgument + ")",
            JsonContext("AppContext", "[JsonSerializable(typeof(Prefs))]") + """

            [JsonSerializable(typeof(Prefs))]
            internal sealed class NoDefaultContext : JsonSerializerContext
            {
                public NoDefaultContext() : base(null) { }
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """);

        var result = RunGenerator(source);

        var diagnostic = result.RunResult.Diagnostics.First(static value => value.Id == "INQ108");
        Assert.Equal(contextArgument, source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }
}
