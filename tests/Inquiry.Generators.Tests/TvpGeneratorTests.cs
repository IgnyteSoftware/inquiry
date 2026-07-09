using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// SQL Server table-valued parameters: <c>Compare.In</c> renders
/// <c>col IN (SELECT [Value] FROM @name)</c> and binds the collection as a TVP via
/// <c>InquiryTvpParameter.Bind</c>. DeleteAll uses the same mechanism.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerInPredicateRendersTvpSubqueryAndBindsTvp()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("[CategoryId] IN (SELECT [Value] FROM @CategoryId)", generatedText);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@CategoryId\", categoryIds);", generatedText);
        Assert.DoesNotContain("InquiryInExpansion", generatedText);
        Assert.DoesNotContain("InquiryArrayParameter", generatedText);
    }

    [Fact]
    public void SqlServerDeleteAllByKeysRendersTvpSubqueryAndBindsTvp()
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
            public sealed class TvpItem
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class TvpItemStore : Inquiry.Stores.InquiryStore<Demo.TvpItem>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TvpItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("[Id] IN (SELECT [Value] FROM @keys)", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@keys\", ids);", text);
        Assert.DoesNotContain("InquiryInExpansion", text);
        Assert.DoesNotContain("InquiryArrayParameter", text);
    }

    [Fact]
    public void SqlServerNotInPredicateStillUsesSentinelExpansion()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.NotIn)]
                public partial Task<IReadOnlyList<Product>> NotInCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("InquiryInExpansion.ExpandNotIn", generatedText);
        Assert.DoesNotContain("InquiryTvpParameter", generatedText);
    }

    [Fact]
    public void SqlServerPredicateMutationInRendersTvpAndBindsTvp()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TvpMutItem")]
            public sealed class TvpMutItem
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn]
                public string Category { get; set; } = string.Empty;
            }

            public partial class TvpMutItemStore : Inquiry.Stores.InquiryStore<Demo.TvpMutItem>
            {
                [InquiryDeleteWhere]
                [InquiryWhere("Category", Compare.In)]
                public partial Task<int> DeleteCategoriesAsync(IReadOnlyList<string> categories, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("TvpMutItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("[Category] IN (SELECT [Value] FROM @Category)", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@Category\", categories);", text);
    }

    [Fact]
    public void SqliteUsesJsonEachNotTvp()
    {
        var source = PredicateSource("""
            [InquirySelectAllByPredicate]
                [InquiryWhere("CategoryId", Compare.In)]
                public partial Task<IReadOnlyList<Product>> InCategoriesAsync(IReadOnlyList<int> categoryIds, CancellationToken cancellationToken = default);
            """);

        var result = RunGenerator(source);
        Assert.Empty(result.RunResult.Diagnostics);

        var generatedText = GeneratedProductStoreText(result);

        Assert.Contains("IN (SELECT value FROM json_each(@CategoryId))", generatedText);
        Assert.Contains("InquiryJsonArrayParameter.Bind", generatedText);
        Assert.DoesNotContain("InquiryTvpParameter", generatedText);
        Assert.DoesNotContain("InquiryInExpansion", generatedText);
    }
}
