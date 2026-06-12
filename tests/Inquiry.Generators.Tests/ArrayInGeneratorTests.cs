using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// PostgreSQL array IN parameters: <c>Compare.In</c> renders <c>col = ANY(@name)</c> and binds the
/// collection as one native array parameter (constant SQL across list lengths, prepared-statement
/// reuse, no per-element parameter cap). Other dialects keep the <c>IN (@name)</c> sentinel +
/// runtime expansion.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void PostgreSqlInPredicateRendersAnyAndBindsArrayParameter()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "PostgreSql");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        // Constant SQL: = ANY(@CategoryId), no IN sentinel.
        Assert.Contains("\\\"CategoryId\\\" = ANY(@CategoryId)", generatedText);
        Assert.DoesNotContain("IN (@CategoryId)", generatedText);

        // The whole collection binds as one array parameter; no runtime text rewrite.
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
        Assert.DoesNotContain("InquiryInExpansion", generatedText);
    }

    [Fact]
    public void SqliteInPredicateKeepsSentinelExpansion()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("\\\"CategoryId\\\" IN (@CategoryId)", generatedText);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"@CategoryId\", categoryIds, Inquiry.MaxParametersPerCommand);", generatedText);
        Assert.DoesNotContain("InquiryArrayParameter", generatedText);
    }

    [Fact]
    public void PostgreSqlDeleteAllByKeysRendersAnyAndBindsArrayParameter()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Id\\\" = ANY(@keys)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@keys\", ids);", text);
        Assert.DoesNotContain("InquiryInExpansion", text);
    }

    [Fact]
    public void PostgreSqlPredicateMutationInRendersAnyAndBindsArrayParameter()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public string Category { get; set; } = string.Empty;
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquiryDeleteWhere]
                [InquiryWhere("Category", Compare.In)]
                public partial Task<int> DeleteCategoriesAsync(IReadOnlyList<string> categories, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("\\\"Category\\\" = ANY(@Category)", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryArrayParameter.Bind(_c, \"@Category\", categories);", text);
    }
}
