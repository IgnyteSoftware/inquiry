using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Commands;
using Inquiry.Entities;
using Inquiry.Materialization;

namespace Inquiry.Benchmarks;

[InquiryAdHoc]
public sealed class NameMappingProductRow
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? SupplierID { get; set; }
    public int? CategoryID { get; set; }
    public string? QuantityPerUnit { get; set; }
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public short? UnitsOnOrder { get; set; }
    public short? ReorderLevel { get; set; }
    public bool Discontinued { get; set; }
}

/// <summary>The ordinal read that generated ad-hoc materializers used before #475.</summary>
internal readonly struct OrdinalProductRowMaterializer : IInquiryEntityMaterializer<NameMappingProductRow>
{
    public NameMappingProductRow Materialize(DbDataReader reader) => new()
    {
        ProductID = reader.GetInt32(0),
        ProductName = reader.GetString(1),
        SupplierID = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        CategoryID = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        QuantityPerUnit = reader.IsDBNull(4) ? null : reader.GetString(4),
        UnitPrice = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
        UnitsInStock = reader.IsDBNull(6) ? null : reader.GetInt16(6),
        UnitsOnOrder = reader.IsDBNull(7) ? null : reader.GetInt16(7),
        ReorderLevel = reader.IsDBNull(8) ? null : reader.GetInt16(8),
        Discontinued = reader.GetBoolean(9),
    };
}

/// <summary>
/// Option 3 of the #420 decision: check the column count and every column name once per result set,
/// then read by fixed ordinal.
/// </summary>
internal readonly struct CheckedOrdinalProductRowMaterializer
    : IInquiryEntityMaterializer<NameMappingProductRow>, IInquiryResultSetBinder<NameMappingProductRow, CheckedOrdinalProductRowMaterializer>
{
    private static readonly string[] _names =
    [
        "ProductID", "ProductName", "SupplierID", "CategoryID", "QuantityPerUnit",
        "UnitPrice", "UnitsInStock", "UnitsOnOrder", "ReorderLevel", "Discontinued",
    ];

    public CheckedOrdinalProductRowMaterializer Bind(DbDataReader reader)
    {
        if (reader.FieldCount != _names.Length)
        {
            throw new InvalidOperationException("Unexpected column count.");
        }

        for (var ordinal = 0; ordinal < _names.Length; ordinal++)
        {
            if (!string.Equals(reader.GetName(ordinal), _names[ordinal], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unexpected column at ordinal {ordinal}.");
            }
        }

        return this;
    }

    public NameMappingProductRow Materialize(DbDataReader reader) => default(OrdinalProductRowMaterializer).Materialize(reader);
}

/// <summary>
/// Measures the per-result-set cost of name mapping for handwritten SQL (#475) on the in-process
/// SQLite database, where library overhead is not hidden by a network round trip. Every leg runs the
/// same pipeline path and SQL. Only the materializer differs:
/// <list type="bullet">
///   <item><b>Ordinal</b> — the fixed-ordinal read used before #475 (baseline).</item>
///   <item><b>CheckedOrdinal</b> — option 3: verify the column names, then read by ordinal.</item>
///   <item><b>NameMapped</b> — option 2, the generated materializer, with columns in property order.</item>
///   <item><b>NameMappedReordered</b> — option 2 with the SELECT list reversed, which takes the reordered read path.</item>
///   <item><b>NameMappedClassPath</b> — option 2 through the DI class materializer that <c>IInquiry.Query*&lt;T&gt;</c> resolves.
///   It adds interface dispatch and the bound materializer allocation.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class AdHocNameMappingBenchmarks
{
    private const string Columns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";
    private const string ReversedColumns =
        "Discontinued, ReorderLevel, UnitsOnOrder, UnitsInStock, UnitPrice, QuantityPerUnit, CategoryID, SupplierID, ProductName, ProductID";
    private const string ListSql = "SELECT " + Columns + " FROM Products";
    private const string ReversedListSql = "SELECT " + ReversedColumns + " FROM Products";
    private const string SingleSql = ListSql + " WHERE ProductID = 1";
    private const string ReversedSingleSql = ReversedListSql + " WHERE ProductID = 1";

    private BenchmarkDatabase _db = null!;
    private IInquiry _inquiry = null!;

    [Params(1000, 100000)] public int Rows { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _inquiry = _db.Inquiry;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // ---- SingleRow ----------------------------------------------------------------------

    [BenchmarkCategory("SingleRow"), Benchmark(Baseline = true)]
    public Task<NameMappingProductRow?> SingleRow_Ordinal()
        => _inquiry.QuerySingleOrDefaultAsync<NameMappingProductRow, OrdinalProductRowMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<NameMappingProductRow?> SingleRow_CheckedOrdinal()
        => _inquiry.QuerySingleOrDefaultAsync<NameMappingProductRow, CheckedOrdinalProductRowMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<NameMappingProductRow?> SingleRow_NameMapped()
        => _inquiry.QuerySingleOrDefaultAsync<NameMappingProductRow, NameMappingProductRowInquiryAdHocStructMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<NameMappingProductRow?> SingleRow_NameMappedReordered()
        => _inquiry.QuerySingleOrDefaultAsync<NameMappingProductRow, NameMappingProductRowInquiryAdHocStructMaterializer>(new InquiryCommand(ReversedSingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<NameMappingProductRow?> SingleRow_NameMappedClassPath()
        => _inquiry.QuerySingleOrDefaultAsync<NameMappingProductRow>(new InquiryCommand(SingleSql));

    // ---- List ---------------------------------------------------------------------------

    [BenchmarkCategory("List"), Benchmark(Baseline = true)]
    public Task<IReadOnlyList<NameMappingProductRow>> List_Ordinal()
        => _inquiry.QueryListAsync<NameMappingProductRow, OrdinalProductRowMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("List"), Benchmark]
    public Task<IReadOnlyList<NameMappingProductRow>> List_CheckedOrdinal()
        => _inquiry.QueryListAsync<NameMappingProductRow, CheckedOrdinalProductRowMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("List"), Benchmark]
    public Task<IReadOnlyList<NameMappingProductRow>> List_NameMapped()
        => _inquiry.QueryListAsync<NameMappingProductRow, NameMappingProductRowInquiryAdHocStructMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("List"), Benchmark]
    public Task<IReadOnlyList<NameMappingProductRow>> List_NameMappedReordered()
        => _inquiry.QueryListAsync<NameMappingProductRow, NameMappingProductRowInquiryAdHocStructMaterializer>(new InquiryCommand(ReversedListSql), default);

    [BenchmarkCategory("List"), Benchmark]
    public Task<IReadOnlyList<NameMappingProductRow>> List_NameMappedClassPath()
        => _inquiry.QueryListAsync<NameMappingProductRow>(new InquiryCommand(ListSql));
}
