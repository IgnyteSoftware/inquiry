using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Benchmarks.PostgreSql;

/// <summary>
/// The bulk-insert tiers on PostgreSQL: cap-respecting multi-row <c>INSERT … VALUES</c> batches
/// (<c>[InquiryInsert]</c>, chunked to stay under the parameter cap — the realistic usage
/// pattern) versus a single binary <c>COPY</c> stream (<c>[InquiryBulkInsert]</c>). Each iteration
/// truncates the table so growth doesn't skew timings.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class BulkInsertBenchmarks
{
    // 2 bound columns per shipper → 800 rows/chunk keeps each batch statement well under the cap.
    private const int InsertAllChunkSize = 800;

    private PostgreSqlBenchmarkDatabase _db = null!;
    private ShipperStore _shippers = null!;
    private IInquiry _inquiry = null!;
    private Shipper[] _rows = null!;

    [Params(5000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = PostgreSqlBenchmarkDatabase.CreateAsync(seedRows: 1).GetAwaiter().GetResult();
        _shippers = _db.GetRequiredService<ShipperStore>(PreparedStatementMode.Auto);
        _inquiry = _db.GetRequiredService<IInquiry>(PreparedStatementMode.Auto);

        _rows = new Shipper[Rows];
        for (var i = 0; i < Rows; i++)
        {
            _rows[i] = new Shipper { CompanyName = "Bulk Shipper " + i, Phone = "(503) 555-" + (1000 + i % 9000) };
        }
    }

    [IterationSetup]
    public void IterationSetup()
        => _inquiry.ExecuteAsync($"TRUNCATE TABLE \"Shippers\" RESTART IDENTITY CASCADE").GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup()
        => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BulkTier"), Benchmark(Baseline = true)]
    public async Task<int> InsertAll_Chunked()
    {
        var total = 0;
        for (var offset = 0; offset < _rows.Length; offset += InsertAllChunkSize)
        {
            var chunk = _rows.Skip(offset).Take(InsertAllChunkSize);
            total += await _shippers.InsertAllAsync(chunk);
        }

        return total;
    }

    [BenchmarkCategory("BulkTier"), Benchmark]
    public Task<long> BulkInsert_BinaryCopy()
        => _shippers.BulkInsertAsync(_rows);
}
