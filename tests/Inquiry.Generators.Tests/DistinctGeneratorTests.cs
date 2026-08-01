using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// DISTINCT emission tests: <c>Distinct = true</c> on <c>[InquirySelectAll]</c>,
/// <c>[InquirySelectAllByField]</c>, and <c>[InquirySelectAllByPredicate]</c> emits
/// <c>SELECT DISTINCT</c> instead of <c>SELECT</c>. Each gets a per-method const since the SQL
/// differs from the shared select. Projections compose with DISTINCT the same way.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string DistinctProductEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TProduct")]
        public sealed class Product
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("Category")]
            public string Category { get; set; } = string.Empty;
        }
        """;

    private static string DistinctProductStore(string methods) =>
        DistinctProductEntity + "\n\npublic partial class DistinctProductStore : Inquiry.Stores.InquiryStore<Demo.Product>\n{\n" + methods + "\n}\n";

    private static string GetDistinctProductStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("DistinctProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void DistinctSelectAllEmitsSelectDistinct_Sqlite()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll(Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT", text);
        Assert.Contains("_sqlSelectAll_SelectDistinctAsync", text);
    }

    [Fact]
    public void DistinctSelectAllByFieldEmitsSelectDistinct_Sqlite()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAllByField("Category", Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctByCategoryAsync(string category, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT \\\"Id\\\", \\\"Name\\\", \\\"Category\\\" FROM \\\"TProduct\\\" WHERE \\\"Category\\\" = @Category", text);
    }

    [Fact]
    public void DistinctSelectAllByPredicateEmitsSelectDistinct_Sqlite()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAllByPredicate(Distinct = true)]
            [InquiryWhere("Name", Compare.Like)]
            public partial Task<IReadOnlyList<Product>> SearchDistinctAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT \\\"Id\\\", \\\"Name\\\", \\\"Category\\\" FROM \\\"TProduct\\\" WHERE \\\"Name\\\" LIKE @Name", text);
    }

    [Fact]
    public void NonDistinctSelectAllDoesNotEmitDistinct_Sqlite()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll]
            public partial Task<IReadOnlyList<Product>> SelectAllAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.DoesNotContain("SELECT DISTINCT", text);
        Assert.Contains("_sqlSelectAll", text);
    }

    [Fact]
    public void DistinctSelectAll_SqlServer()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll(Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctAsync(CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT [Id], [Name], [Category] FROM [TProduct]", text);
    }

    [Fact]
    public void DistinctSelectAll_PostgreSql()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll(Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctAsync(CancellationToken cancellationToken = default);
            """), dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT", text);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void DistinctSelectAll_MySql(string dialect)
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll(Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctAsync(CancellationToken cancellationToken = default);
            """), dialect: dialect);
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT", text);
    }

    [Fact]
    public void DistinctSelectAll_Oracle()
    {
        var result = RunGenerator(DistinctProductStore("""
            [InquirySelectAll(Distinct = true)]
            public partial Task<IReadOnlyList<Product>> SelectDistinctAsync(CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetDistinctProductStore(result);

        Assert.Contains("SELECT DISTINCT", text);
    }

    [Fact]
    public void DistinctProjectionSelectAllEmitsSelectDistinct_Sqlite()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TProduct")]
            public sealed class Product
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("Category")]
                public string Category { get; set; } = string.Empty;
            }

            [InquiryProjection(typeof(Product))]
            public sealed record ProductCategory
            {
                [InquiryColumn("Category")]
                public string Category { get; init; } = string.Empty;
            }

            public partial class DistinctProductStore : Inquiry.Stores.InquiryStore<Demo.Product>
            {
                [InquirySelectAll(Distinct = true)]
                public partial Task<IReadOnlyList<ProductCategory>> ListDistinctCategoriesAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DistinctProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("SELECT DISTINCT \\\"Category\\\" FROM \\\"TProduct\\\"", text);
    }

    [Fact]
    public void DistinctProjectionSelectAllByFieldEmitsSelectDistinct_Sqlite()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TProduct")]
            public sealed class Product
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("Category")]
                public string Category { get; set; } = string.Empty;
            }

            [InquiryProjection(typeof(Product))]
            public sealed record ProductName
            {
                [InquiryColumn("Name")]
                public string Name { get; init; } = string.Empty;
            }

            public partial class DistinctProductStore : Inquiry.Stores.InquiryStore<Demo.Product>
            {
                [InquirySelectAllByField("Category", Distinct = true)]
                public partial Task<IReadOnlyList<ProductName>> DistinctNamesByCategoryAsync(string category, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DistinctProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("SELECT DISTINCT \\\"Name\\\" FROM \\\"TProduct\\\" WHERE \\\"Category\\\" = @Category", text);
    }
}
