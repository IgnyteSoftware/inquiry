using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Negated predicate operators: <c>Compare.NotLike</c> renders <c>NOT (col LIKE @p)</c> (reusing the
/// LIKE hook so dialect ESCAPE handling stays consistent) and <c>Compare.NotBetween</c> renders
/// <c>col NOT BETWEEN @lo AND @hi</c>. Both compose with AND/OR like any criterion; NotLike requires a
/// string column, NotBetween two type-matched parameters.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ProductEntity = """
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
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("Qty")]
            public int Qty { get; set; }
        }

        """;

    private static string ProductStore(string methods) => ProductEntity + """
        public partial class ProductStore : Inquiry.Stores.InquiryStore<Demo.Product>
        {
        """ + "\n" + methods + "\n}\n";

    private static string GetProductStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ProductStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void NotLikeRendersNegatedLike_Sqlite()
    {
        var result = RunGenerator(ProductStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.NotLike)]
            public partial Task<IReadOnlyList<Product>> NameNotLikeAsync(string name, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetProductStore(result);

        Assert.Contains("WHERE NOT (\\\"Name\\\" LIKE @Name)", text);
    }

    [Fact]
    public void NotBetweenRendersNegatedBetween_Sqlite()
    {
        var result = RunGenerator(ProductStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Qty", Compare.NotBetween)]
            public partial Task<IReadOnlyList<Product>> QtyNotBetweenAsync(int low, int high, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetProductStore(result);

        Assert.Contains("WHERE \\\"Qty\\\" NOT BETWEEN @Qty_lo AND @Qty_hi", text);
    }

    [Fact]
    public void NegatedOperatorsComposeWithAnd_Sqlite()
    {
        var result = RunGenerator(ProductStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.NotLike)]
            [InquiryWhere("Qty", Compare.NotBetween)]
            public partial Task<IReadOnlyList<Product>> SearchAsync(string name, int low, int high, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetProductStore(result);

        Assert.Contains("WHERE NOT (\\\"Name\\\" LIKE @Name) AND \\\"Qty\\\" NOT BETWEEN @Qty_lo AND @Qty_hi", text);
    }

    [Fact]
    public void OracleNotLikeUsesColonSigil()
    {
        var result = RunGenerator(ProductStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Name", Compare.NotLike)]
            public partial Task<IReadOnlyList<Product>> NameNotLikeAsync(string name, CancellationToken cancellationToken = default);
            """), dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetProductStore(result);

        Assert.Contains("WHERE NOT (Name LIKE :Name)", text);
    }

    [Fact]
    public void NotLikeOnNonStringColumnReportsDiagnostic()
    {
        // NotLike, like Like, requires a string column.
        var result = RunGenerator(ProductStore("""
            [InquirySelectAllByPredicate]
            [InquiryWhere("Qty", Compare.NotLike)]
            public partial Task<IReadOnlyList<Product>> BadAsync(int qty, CancellationToken cancellationToken = default);
            """));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
