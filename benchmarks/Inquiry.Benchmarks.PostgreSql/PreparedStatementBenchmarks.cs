using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Commands;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// PostgreSQL prepared-statement comparison for Inquiry's own pipeline: explicit unprepared execution
/// versus the default <see cref="PreparedStatementMode.Auto"/> on Npgsql, where prepared statements
/// survive in the physical pooled connection.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class PreparedStatementBenchmarks
{
    private const int TargetShipperId = 1;
    private const int TargetProductId = 1;
    private const string TargetCategoryName = "Category 0";
    private const string TargetSupplierName = "Supplier 0";

    private static readonly InquiryCommand MultiJoinProductCommand = InquirySql.Sql($"""
        SELECT
            p."ProductID",
            p."ProductName",
            p."SupplierID",
            p."CategoryID",
            p."QuantityPerUnit",
            p."UnitPrice",
            p."UnitsInStock",
            p."UnitsOnOrder",
            p."ReorderLevel",
            p."Discontinued"
        FROM "Products" p
        INNER JOIN "Categories" c ON c."CategoryID" = p."CategoryID"
        INNER JOIN "Suppliers" s ON s."SupplierID" = p."SupplierID"
        WHERE p."ProductID" = {TargetProductId}
          AND c."CategoryName" = {TargetCategoryName}
          AND s."CompanyName" = {TargetSupplierName}
        """);

    private PostgreSqlBenchmarkDatabase _db = null!;
    private ShipperStore _shippersWithoutPreparation = null!;
    private ShipperStore _shippersWithPreparation = null!;
    private IInquiry _inquiryWithoutPreparation = null!;
    private IInquiry _inquiryWithPreparation = null!;

    [Params(1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = PostgreSqlBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _shippersWithoutPreparation = _db.GetRequiredService<ShipperStore>(PreparedStatementMode.None);
        _shippersWithPreparation = _db.GetRequiredService<ShipperStore>(PreparedStatementMode.Auto);
        _inquiryWithoutPreparation = _db.GetRequiredService<IInquiry>(PreparedStatementMode.None);
        _inquiryWithPreparation = _db.GetRequiredService<IInquiry>(PreparedStatementMode.Auto);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("SimplePointRead"), Benchmark(Baseline = true)]
    public Task<Shipper?> SimplePointRead_None()
        => _shippersWithoutPreparation.SelectByKeyAsync(TargetShipperId);

    [BenchmarkCategory("SimplePointRead"), Benchmark]
    public Task<Shipper?> SimplePointRead_Auto()
        => _shippersWithPreparation.SelectByKeyAsync(TargetShipperId);

    [BenchmarkCategory("MultiJoinPointRead"), Benchmark(Baseline = true)]
    public Task<Product?> MultiJoinPointRead_None()
        => _inquiryWithoutPreparation.QuerySingleOrDefaultAsync<Product>(MultiJoinProductCommand);

    [BenchmarkCategory("MultiJoinPointRead"), Benchmark]
    public Task<Product?> MultiJoinPointRead_Auto()
        => _inquiryWithPreparation.QuerySingleOrDefaultAsync<Product>(MultiJoinProductCommand);
}
