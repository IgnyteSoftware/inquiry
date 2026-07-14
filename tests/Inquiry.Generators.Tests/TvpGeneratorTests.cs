using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// SQL Server table-valued parameters: <c>Compare.In</c> renders
/// <c>col IN (SELECT [Value] FROM @name)</c> and binds the collection as a TVP via
/// <c>InquiryTvpParameter.Bind</c>. DeleteAll uses the same mechanism.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string IntTvpTypeName = "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]";
    private const string BigIntTvpTypeName = "[dbo].[Inquiry_Tvp_7fd6c8a95588d206e3cbdd54c1dd765afffea824af43008e3f37179b9e033cfc]";
    private const string StringTvpTypeName = "[dbo].[Inquiry_Tvp_3dd8e5db30a8f837bbccaa41878576af742a2993aa5655c580e8a7ed2e31ea71]";

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
        Assert.Contains($"global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@CategoryId\", categoryIds, \"{IntTvpTypeName}\", _inquiryTvpDescriptor_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c);", generatedText);
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
        Assert.Contains($"global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@keys\", ids, \"{BigIntTvpTypeName}\", _inquiryTvpDescriptor_7fd6c8a95588d206e3cbdd54c1dd765afffea824af43008e3f37179b9e033cfc);", text);
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
        Assert.Contains($"global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@Category\", categories, \"{StringTvpTypeName}\", _inquiryTvpDescriptor_3dd8e5db30a8f837bbccaa41878576af742a2993aa5655c580e8a7ed2e31ea71);", text);
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
