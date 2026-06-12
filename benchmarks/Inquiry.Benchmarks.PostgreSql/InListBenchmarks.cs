using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Commands;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Parameters;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// PostgreSQL IN-list mechanics: the generated <c>Compare.In</c> path now binds the collection as a
/// single native array parameter against constant <c>= ANY(@p)</c> SQL, versus the legacy sentinel
/// expansion that rewrites the command text and adds one parameter per element. Each invocation
/// cycles through list lengths (1/5/20/100) to model real workloads where IN cardinality varies —
/// the expansion path produces a distinct SQL text per length (defeating prepared-statement reuse),
/// while the array path keeps one constant, preparable statement.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class InListBenchmarks
{
    private PostgreSqlBenchmarkDatabase _db = null!;
    private ProductStore _products = null!;
    private IInquiry _inquiry = null!;
    private int[][] _idLists = null!;
    private int _cursor;

    [Params(1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = PostgreSqlBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _products = _db.GetRequiredService<ProductStore>(PreparedStatementMode.Auto);
        _inquiry = _db.GetRequiredService<IInquiry>(PreparedStatementMode.Auto);

        var min = _inquiry.ExecuteScalarAsync<int>(InquirySql.Sql($"SELECT MIN(\"CategoryID\") FROM \"Categories\"")).GetAwaiter().GetResult();
        var max = _inquiry.ExecuteScalarAsync<int>(InquirySql.Sql($"SELECT MAX(\"CategoryID\") FROM \"Categories\"")).GetAwaiter().GetResult();

        // Category-id lists of varying cardinality (values may repeat — IN semantics are unaffected).
        _idLists = new int[4][];
        var sizes = new[] { 1, 5, 20, 100 };
        for (var s = 0; s < sizes.Length; s++)
        {
            var list = new int[sizes[s]];
            for (var i = 0; i < list.Length; i++)
            {
                list[i] = min + (i % (max - min + 1));
            }

            _idLists[s] = list;
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private int[] NextList() => _idLists[_cursor++ & 3];

    /// <summary>The shipped path: constant <c>= ANY(@CategoryId)</c> SQL + one array parameter.</summary>
    [BenchmarkCategory("VaryingInList"), Benchmark(Baseline = true)]
    public Task<IReadOnlyList<Product>> ArrayAnyParameter()
        => _products.InCategoriesAsync(NextList());

    /// <summary>
    /// The legacy mechanism (still used on non-array dialects): per-call command-text rewrite into
    /// one placeholder per element. Same pipeline, same query shape — isolates the IN mechanics.
    /// </summary>
    [BenchmarkCategory("VaryingInList"), Benchmark]
    public Task<IReadOnlyList<Product>> ExpandedSentinel()
    {
        var ids = NextList();
        var command = new InquiryCommand(
            """
            SELECT "ProductID", "ProductName", "SupplierID", "CategoryID", "QuantityPerUnit", "UnitPrice", "UnitsInStock", "UnitsOnOrder", "ReorderLevel", "Discontinued"
            FROM "Products" WHERE "CategoryID" IN (@ids)
            """,
            cmd => InquiryInExpansion.Expand(cmd, "@ids", ids));
        return _inquiry.QueryListAsync<Product>(command);
    }
}
