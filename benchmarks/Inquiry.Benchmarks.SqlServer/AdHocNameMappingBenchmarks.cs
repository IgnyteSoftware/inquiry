using System.Data.Common;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Commands;
using Inquiry.Materialization;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>The ordinal read that generated ad-hoc materializers used before #475.</summary>
internal readonly struct OrdinalSequentialRowMaterializer : IInquiryEntityMaterializer<SequentialBenchmarkRow>
{
    public SequentialBenchmarkRow Materialize(DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        C01 = reader.GetInt32(1),
        C02 = reader.GetInt32(2),
        C03 = reader.GetInt32(3),
        C04 = reader.GetInt32(4),
        C05 = reader.GetInt32(5),
        C06 = reader.GetInt32(6),
        C07 = reader.GetInt32(7),
        C08 = reader.GetInt32(8),
        C09 = reader.GetInt32(9),
        C10 = reader.GetInt32(10),
        C11 = reader.GetInt32(11),
        C12 = reader.GetInt32(12),
        Payload = reader.GetFieldValue<byte[]>(13),
    };
}

/// <summary>
/// Option 3 of the #420 decision: check the column count and every column name once per result set,
/// then read by fixed ordinal.
/// </summary>
internal readonly struct CheckedOrdinalSequentialRowMaterializer
    : IInquiryEntityMaterializer<SequentialBenchmarkRow>, IInquiryResultSetBinder<SequentialBenchmarkRow, CheckedOrdinalSequentialRowMaterializer>
{
    private static readonly string[] _names =
        ["Id", "C01", "C02", "C03", "C04", "C05", "C06", "C07", "C08", "C09", "C10", "C11", "C12", "Payload"];

    public CheckedOrdinalSequentialRowMaterializer Bind(DbDataReader reader)
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

    public SequentialBenchmarkRow Materialize(DbDataReader reader) => default(OrdinalSequentialRowMaterializer).Materialize(reader);
}

/// <summary>
/// Measures name mapping for handwritten SQL (#475) on wide rows whose last column is a 64 KB
/// <c>VARBINARY(MAX)</c>, read with <c>SequentialAccess</c>. Every leg runs the same pipeline path and
/// SQL; only the materializer differs. <b>Ordinal</b> is the pre-#475 read, <b>CheckedOrdinal</b> is
/// option 3, and <b>NameMapped</b> is the generated option 2 materializer. <b>NameMappedReordered</b>
/// reverses the SELECT list, so the large column comes first and the reordered read path runs.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class AdHocNameMappingBenchmarks
{
    private const string Columns = "Id, C01, C02, C03, C04, C05, C06, C07, C08, C09, C10, C11, C12, Payload";
    private const string ReversedColumns = "Payload, C12, C11, C10, C09, C08, C07, C06, C05, C04, C03, C02, C01, Id";
    private const string ListSql = "SELECT " + Columns + " FROM BenchmarkSequentialAdHoc ORDER BY Id";
    private const string ReversedListSql = "SELECT " + ReversedColumns + " FROM BenchmarkSequentialAdHoc ORDER BY Id";
    private const string SingleSql = "SELECT " + Columns + " FROM BenchmarkSequentialAdHoc WHERE Id = 1";
    private const string ReversedSingleSql = "SELECT " + ReversedColumns + " FROM BenchmarkSequentialAdHoc WHERE Id = 1";

    private SqlServerBenchmarkDatabase _db = null!;
    private IInquiry _inquiry = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = SqlServerBenchmarkDatabase.CreateAsync(seedRows: 1000).GetAwaiter().GetResult();
        _inquiry = _db.Inquiry;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // ---- SingleRow ----------------------------------------------------------------------

    [BenchmarkCategory("SingleRow"), Benchmark(Baseline = true)]
    public Task<SequentialBenchmarkRow?> SingleRow_Ordinal()
        => _inquiry.QuerySingleOrDefaultAsync<SequentialBenchmarkRow, OrdinalSequentialRowMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<SequentialBenchmarkRow?> SingleRow_CheckedOrdinal()
        => _inquiry.QuerySingleOrDefaultAsync<SequentialBenchmarkRow, CheckedOrdinalSequentialRowMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<SequentialBenchmarkRow?> SingleRow_NameMapped()
        => _inquiry.QuerySingleOrDefaultAsync<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(new InquiryCommand(SingleSql), default);

    [BenchmarkCategory("SingleRow"), Benchmark]
    public Task<SequentialBenchmarkRow?> SingleRow_NameMappedReordered()
        => _inquiry.QuerySingleOrDefaultAsync<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(new InquiryCommand(ReversedSingleSql), default);

    // ---- WideList -----------------------------------------------------------------------

    [BenchmarkCategory("WideList"), Benchmark(Baseline = true)]
    public Task<IReadOnlyList<SequentialBenchmarkRow>> WideList_Ordinal()
        => _inquiry.QueryListAsync<SequentialBenchmarkRow, OrdinalSequentialRowMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("WideList"), Benchmark]
    public Task<IReadOnlyList<SequentialBenchmarkRow>> WideList_CheckedOrdinal()
        => _inquiry.QueryListAsync<SequentialBenchmarkRow, CheckedOrdinalSequentialRowMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("WideList"), Benchmark]
    public Task<IReadOnlyList<SequentialBenchmarkRow>> WideList_NameMapped()
        => _inquiry.QueryListAsync<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(new InquiryCommand(ListSql), default);

    [BenchmarkCategory("WideList"), Benchmark]
    public Task<IReadOnlyList<SequentialBenchmarkRow>> WideList_NameMappedReordered()
        => _inquiry.QueryListAsync<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(new InquiryCommand(ReversedListSql), default);

    // ---- FirstRowThenDispose ------------------------------------------------------------

    [BenchmarkCategory("FirstRowThenDispose"), Benchmark(Baseline = true)]
    public Task<int> FirstRow_Ordinal() => FirstRowAsync<OrdinalSequentialRowMaterializer>(ListSql);

    [BenchmarkCategory("FirstRowThenDispose"), Benchmark]
    public Task<int> FirstRow_CheckedOrdinal() => FirstRowAsync<CheckedOrdinalSequentialRowMaterializer>(ListSql);

    [BenchmarkCategory("FirstRowThenDispose"), Benchmark]
    public Task<int> FirstRow_NameMapped() => FirstRowAsync<SequentialBenchmarkRowInquiryAdHocStructMaterializer>(ListSql);

    [BenchmarkCategory("FirstRowThenDispose"), Benchmark]
    public Task<int> FirstRow_NameMappedReordered() => FirstRowAsync<SequentialBenchmarkRowInquiryAdHocStructMaterializer>(ReversedListSql);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1751", Justification = "The benchmark measures reading one row and then disposing the stream, so the loop returns on its first pass.")]
    private async Task<int> FirstRowAsync<TMaterializer>(string sql)
        where TMaterializer : struct, IInquiryEntityMaterializer<SequentialBenchmarkRow>
    {
#pragma warning disable INQ048 // Callers pass one of the SQL constants above.
        await foreach (var row in _inquiry.QueryAsync<SequentialBenchmarkRow, TMaterializer>(new InquiryCommand(sql), default))
#pragma warning restore INQ048
        {
            return row.Id + row.Payload.Length;
        }

        return 0;
    }
}
