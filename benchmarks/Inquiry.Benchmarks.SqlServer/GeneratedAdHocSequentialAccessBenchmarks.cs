using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Order;
using Inquiry.Commands;
using Inquiry.Entities;
using Inquiry.Materialization;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;

namespace Inquiry.Benchmarks.SqlServer;

[InquiryAdHoc]
public sealed class SequentialBenchmarkRow
{
    public int Id { get; set; }
    public int C01 { get; set; }
    public int C02 { get; set; }
    public int C03 { get; set; }
    public int C04 { get; set; }
    public int C05 { get; set; }
    public int C06 { get; set; }
    public int C07 { get; set; }
    public int C08 { get; set; }
    public int C09 { get; set; }
    public int C10 { get; set; }
    public int C11 { get; set; }
    public int C12 { get; set; }
    public byte[] Payload { get; set; } = [];
}

public sealed class BufferedSequentialBenchmarkRow
{
    public int Id { get; set; }
    public int C01 { get; set; }
    public int C02 { get; set; }
    public int C03 { get; set; }
    public int C04 { get; set; }
    public int C05 { get; set; }
    public int C06 { get; set; }
    public int C07 { get; set; }
    public int C08 { get; set; }
    public int C09 { get; set; }
    public int C10 { get; set; }
    public int C11 { get; set; }
    public int C12 { get; set; }
    public byte[] Payload { get; set; } = [];
}

internal sealed class BufferedSequentialBenchmarkRowMaterializer : IInquiryEntityMaterializer<BufferedSequentialBenchmarkRow>
{
    public BufferedSequentialBenchmarkRow Materialize(DbDataReader reader) => new()
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
/// Isolates generated ad-hoc class dispatch, provider buffering, and early stream disposal over
/// identical SQL and rows. The generated and custom DTOs have the same property layout and read work;
/// only the generated materializer opts into sequential access.
/// </summary>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 4, printSource: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class GeneratedAdHocSequentialAccessBenchmarks
{
    private const string SelectSql =
        "SELECT Id, C01, C02, C03, C04, C05, C06, C07, C08, C09, C10, C11, C12, Payload " +
        "FROM BenchmarkSequentialAdHoc ORDER BY Id";

    private SqlServerBenchmarkDatabase _db = null!;
    private IInquiry _inquiry = null!;
    private DbDataReader _dispatchReader = null!;
    private IInquiryEntityMaterializer<SequentialBenchmarkRow> _classMaterializer = null!;
    private SequentialBenchmarkRowInquiryAdHocStructMaterializer _structMaterializer = default;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = SqlServerBenchmarkDatabase.CreateAsync(seedRows: 1000).GetAwaiter().GetResult();
        _inquiry = _db.Inquiry;
        _dispatchReader = CreateDispatchReader();
        _classMaterializer = new SequentialBenchmarkRowInquiryAdHocMaterializer();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _dispatchReader.Dispose();
        _db.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("MaterializerDispatch"), Benchmark(Baseline = true)]
    public long GeneratedClassInterfaceMaterialize()
        => Checksum(_classMaterializer.Materialize(_dispatchReader));

    [BenchmarkCategory("MaterializerDispatch"), Benchmark]
    public long GeneratedStructMaterialize()
        => Checksum(Materialize<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(
            _dispatchReader,
            _structMaterializer));

    [BenchmarkCategory("EndToEndAdHocPath"), Benchmark(Baseline = true)]
    public async Task<long> GeneratedClassInterfaceList()
        => Checksum(await _inquiry.QueryListAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)));

    [BenchmarkCategory("EndToEndAdHocPath"), Benchmark]
    public async Task<long> GeneratedStructList()
        => Checksum(await _inquiry.QueryListAsync<SequentialBenchmarkRow, SequentialBenchmarkRowInquiryAdHocStructMaterializer>(
            new InquiryCommand(SelectSql), default));

    [BenchmarkCategory("InquiryBuffering"), Benchmark(Baseline = true)]
    public async Task<long> CustomBufferedClassList()
        => Checksum(await _inquiry.QueryListAsync<BufferedSequentialBenchmarkRow>(new InquiryCommand(SelectSql)));

    [BenchmarkCategory("InquiryBuffering"), Benchmark]
    public async Task<long> GeneratedSequentialClassList()
        => Checksum(await _inquiry.QueryListAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)));

    [BenchmarkCategory("AdoBufferingFloor"), Benchmark(Baseline = true)]
    public Task<long> RawAdoBuffered() => ReadRawAsync(CommandBehavior.SingleResult);

    [BenchmarkCategory("AdoBufferingFloor"), Benchmark]
    public Task<long> RawAdoSequential()
        => ReadRawAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);

    [BenchmarkCategory("ConsumptionMode"), Benchmark(Baseline = true)]
    public async Task<long> GeneratedBufferedListConsumption()
        => Checksum(await _inquiry.QueryListAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)));

    [BenchmarkCategory("ConsumptionMode"), Benchmark]
    public async Task<long> GeneratedFullStreamConsumption()
    {
        long checksum = 0;
        await foreach (var row in _inquiry.QueryAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)))
        {
            checksum += Checksum(row);
        }
        return checksum;
    }

    [BenchmarkCategory("PartialStream"), Benchmark(Baseline = true)]
    public async Task<long> GeneratedFullStream()
    {
        long checksum = 0;
        await foreach (var row in _inquiry.QueryAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)))
        {
            checksum += Checksum(row);
        }
        return checksum;
    }

    [BenchmarkCategory("PartialStream"), Benchmark]
    public async Task<long> GeneratedFirstRowThenDispose()
    {
        await foreach (var row in _inquiry.QueryAsync<SequentialBenchmarkRow>(new InquiryCommand(SelectSql)))
        {
            return Checksum(row);
        }
        return 0;
    }

    private async Task<long> ReadRawAsync(CommandBehavior behavior)
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql;
        await using var reader = await command.ExecuteReaderAsync(behavior);
        long checksum = 0;
        while (await reader.ReadAsync()) checksum += Checksum(ReadRow(reader));
        return checksum;
    }

    private static T Materialize<T, TMaterializer>(DbDataReader reader, TMaterializer materializer)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
        => materializer.Materialize(reader);

    private static DbDataReader CreateDispatchReader()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        for (var ordinal = 1; ordinal <= 12; ordinal++)
        {
            table.Columns.Add($"C{ordinal:00}", typeof(int));
        }
        table.Columns.Add("Payload", typeof(byte[]));

        var values = new object[14];
        values[0] = 1;
        for (var ordinal = 1; ordinal <= 12; ordinal++) values[ordinal] = 100 + ordinal;
        var payload = new byte[256];
        for (var index = 0; index < payload.Length; index++) payload[index] = (byte)index;
        values[13] = payload;
        table.Rows.Add(values);

        var reader = table.CreateDataReader();
        if (!reader.Read()) throw new InvalidOperationException("Dispatch benchmark row was not created.");
        return reader;
    }

    private static SequentialBenchmarkRow ReadRow(DbDataReader reader) => new()
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

    private static long Checksum(IReadOnlyList<SequentialBenchmarkRow> rows)
    {
        long checksum = 0;
        foreach (var row in rows) checksum += Checksum(row);
        return checksum;
    }

    private static long Checksum(IReadOnlyList<BufferedSequentialBenchmarkRow> rows)
    {
        long checksum = 0;
        foreach (var row in rows) checksum += Checksum(row);
        return checksum;
    }

    private static long Checksum(SequentialBenchmarkRow row)
        => Checksum(
            row.Id + row.C01 + row.C02 + row.C03 + row.C04 + row.C05 + row.C06 +
            row.C07 + row.C08 + row.C09 + row.C10 + row.C11 + row.C12,
            row.Payload);

    private static long Checksum(BufferedSequentialBenchmarkRow row)
        => Checksum(
            row.Id + row.C01 + row.C02 + row.C03 + row.C04 + row.C05 + row.C06 +
            row.C07 + row.C08 + row.C09 + row.C10 + row.C11 + row.C12,
            row.Payload);

    private static long Checksum(long scalarChecksum, byte[] payload)
    {
        var checksum = scalarChecksum + payload.Length;
        for (var index = 0; index < payload.Length; index++) checksum += payload[index];
        return checksum;
    }
}
