using Inquiry.Generators.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void Duplicate_simple_names_in_distinct_namespaces_emit_independent_sources()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Alpha
            {
                [InquiryTable("AlphaCustomers")]
                public sealed class Customer
                {
                    [InquiryKey] public int Id { get; set; }
                }

                [InquiryProjection(typeof(Customer))]
                public sealed class Summary
                {
                    [InquiryColumn] public int Id { get; set; }
                }

                [InquiryAdHoc]
                public sealed class Report
                {
                    public int Count { get; set; }
                }

                public partial class CustomerStore : InquiryStore<Customer>
                {
                    [InquirySelectAll]
                    public partial Task<IReadOnlyList<Customer>> SelectAllAsync(CancellationToken cancellationToken = default);
                }
            }

            namespace Beta
            {
                [InquiryTable("BetaCustomers")]
                public sealed class Customer
                {
                    [InquiryKey] public int Id { get; set; }
                }

                [InquiryProjection(typeof(Customer))]
                public sealed class Summary
                {
                    [InquiryColumn] public int Id { get; set; }
                }

                [InquiryAdHoc]
                public sealed class Report
                {
                    public int Count { get; set; }
                }

                public partial class CustomerStore : InquiryStore<Customer>
                {
                    [InquirySelectAll]
                    public partial Task<IReadOnlyList<Customer>> SelectAllAsync(CancellationToken cancellationToken = default);
                }
            }
            """;

        var result = RunGenerator(source);

        AssertNoErrors(result);
        var hintNames = result.RunResult.Results
            .SelectMany(static generator => generator.GeneratedSources)
            .Select(static generated => generated.HintName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Alpha.Customer.InquiryEntity.g.cs", hintNames);
        Assert.Contains("Beta.Customer.InquiryEntity.g.cs", hintNames);
        Assert.Contains("Alpha.CustomerStore.InquiryStore.g.cs", hintNames);
        Assert.Contains("Beta.CustomerStore.InquiryStore.g.cs", hintNames);
        Assert.Contains("Alpha.Summary.InquiryProjection.g.cs", hintNames);
        Assert.Contains("Beta.Summary.InquiryProjection.g.cs", hintNames);
        Assert.Contains("Alpha.Report.InquiryAdHoc.g.cs", hintNames);
        Assert.Contains("Beta.Report.InquiryAdHoc.g.cs", hintNames);
    }

    [Fact]
    public void Hint_name_identity_includes_containing_types_and_generic_arity()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("namespace Demo; public class Outer<T> { public class Inner<TValue> { } }");
        var compilation = CSharpCompilation.Create("HintNameIdentity", [syntaxTree]);
        var symbol = compilation.GetTypeByMetadataName("Demo.Outer`1+Inner`1");

        var hintName = GeneratorHelpers.GetHintName(symbol!, "InquiryEntity");

        Assert.Equal("Demo.Outer`1+Inner`1.InquiryEntity.g.cs", hintName);
    }

    [Fact]
    public void Nested_supported_types_emit_metadata_qualified_hint_names()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            public static class Outer
            {
                [InquiryTable("Customers")]
                public sealed class Customer
                {
                    [InquiryKey] public int Id { get; set; }
                }

                [InquiryProjection(typeof(Customer))]
                public sealed class Summary
                {
                    [InquiryColumn] public int Id { get; set; }
                }

                [InquiryAdHoc]
                public sealed class Report
                {
                    public int Count { get; set; }
                }
            }
            """;

        var result = RunGenerator(source);

        AssertNoErrors(result);
        var hintNames = result.RunResult.Results
            .SelectMany(static generator => generator.GeneratedSources)
            .Select(static generated => generated.HintName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Demo.Outer+Customer.InquiryEntity.g.cs", hintNames);
        Assert.Contains("Demo.Outer+Summary.InquiryProjection.g.cs", hintNames);
        Assert.Contains("Demo.Outer+Report.InquiryAdHoc.g.cs", hintNames);
    }

    [Fact]
    public void Duplicate_unmapped_store_names_emit_independent_invalid_entity_stubs()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Stores;

            namespace Alpha
            {
                public sealed class Unmapped { public int Id { get; set; } }

                public partial class UnmappedStore : InquiryStore<Unmapped>
                {
                    [InquirySelectAll]
                    public partial Task<IReadOnlyList<Unmapped>> SelectAllAsync(CancellationToken cancellationToken = default);
                }
            }

            namespace Beta
            {
                public sealed class Unmapped { public int Id { get; set; } }

                public partial class UnmappedStore : InquiryStore<Unmapped>
                {
                    [InquirySelectAll]
                    public partial Task<IReadOnlyList<Unmapped>> SelectAllAsync(CancellationToken cancellationToken = default);
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "AD0001");
        Assert.DoesNotContain(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "AD0001");
        Assert.Equal(2, result.RunResult.Diagnostics.Count(static diagnostic => diagnostic.Id == "INQ008"));
        var stores = result.RunResult.Results
            .SelectMany(static generator => generator.GeneratedSources)
            .Where(static generated => generated.HintName.EndsWith(".InquiryStore.g.cs", StringComparison.Ordinal))
            .ToDictionary(static generated => generated.HintName, static generated => generated.SourceText.ToString(), StringComparer.Ordinal);
        Assert.Contains("Alpha.UnmappedStore.InquiryStore.g.cs", stores.Keys);
        Assert.Contains("Beta.UnmappedStore.InquiryStore.g.cs", stores.Keys);
        Assert.All(stores.Values, static generated => Assert.Contains("throw new global::System.NotSupportedException", generated, StringComparison.Ordinal));
    }
}
