using System.Data;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Benchmarks.Contracts;

namespace Inquiry.Benchmarks.SqlServer;

[MemoryDiagnoser]
[InvocationCount(1)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BatchMutationStrategyBenchmarks
{
    private SqlServerBenchmarkDatabase _database = null!;
    private BatchMutationBenchmarkStore _store = null!;
    private SqlServerBatchMutationStrategyRunner _runner = null!;
    private BatchMutationBenchmarkItem[] _insertItems = null!;
    private BatchMutationBenchmarkItem[] _updateItems = null!;
    private int[] _deleteIds = null!;
    private string _insertSql = null!;
    private DataTable _deleteTvp = null!;

    [Params(1, 10, 100, 1000)]
    public int Rows;

    public bool CanCreateBatch => _runner.CanCreateBatch;

    [GlobalSetup]
    public void GlobalSetup()
    {
        PrecomputedTransportBenchmarkContract.Validate(GetType());
        _database = SqlServerBenchmarkDatabase.CreateAsync(1).GetAwaiter().GetResult();
        _store = _database.BatchMutations;
        _runner = new SqlServerBatchMutationStrategyRunner(_database.ConnectionString);
        _runner.ProbeAsync().GetAwaiter().GetResult();
        Console.WriteLine($"SqlConnection.CanCreateBatch = {_runner.CanCreateBatch}");
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _runner.ResetAsync(Rows).GetAwaiter().GetResult();
        _insertItems = CreateItems(Rows, 100_001, "Inserted");
        _updateItems = CreateItems(Rows, 1, "Updated");
        _deleteIds = Enumerable.Range(1, Rows).ToArray();
        _insertSql = BuildInsertSql(Rows);
        _deleteTvp?.Dispose();
        _deleteTvp = BuildDeleteTvp(_deleteIds);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _deleteTvp?.Dispose();
        _database.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("Insert"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedInsertAll() => RequireAsync(_store.InsertAllAsync(_insertItems));
    [BenchmarkCategory("Insert"), Benchmark]
    public Task<int> Direct_ReusedPreparedInsert() => RequireAsync(_runner.InsertReusedPreparedAsync(_insertItems));
    [BenchmarkCategory("Insert"), Benchmark]
    public Task<int> Native_DbBatchInsert() => RequireAsync(RequireBatchSupport(_runner.InsertDbBatchAsync, _insertItems));
    [BenchmarkCategory("Insert"), Benchmark, PrecomputedTransportBenchmark]
    public Task<int> Raw_PrecomputedMultiRowInsertFloor() => RequireAsync(_runner.InsertMultiRowAsync(_insertSql, _insertItems));
    [BenchmarkCategory("Insert"), Benchmark]
    public Task<int> Raw_EndToEndMultiRowInsert() => RequireAsync(_runner.InsertMultiRowAsync(BuildInsertSql(Rows), _insertItems));

    [BenchmarkCategory("Update"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedUpdateAll() => RequireAsync(_store.UpdateAllAsync(_updateItems));
    [BenchmarkCategory("Update"), Benchmark]
    public Task<int> Direct_ReusedPreparedUpdate() => RequireAsync(_runner.UpdateReusedPreparedAsync(_updateItems));
    [BenchmarkCategory("Update"), Benchmark]
    public Task<int> Native_DbBatchUpdate() => RequireAsync(RequireBatchSupport(_runner.UpdateDbBatchAsync, _updateItems));

    [BenchmarkCategory("Delete"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedDeleteAll() => RequireAsync(_store.DeleteAllAsync(_deleteIds));
    [BenchmarkCategory("Delete"), Benchmark]
    public Task<int> Direct_ReusedPreparedDelete() => RequireAsync(_runner.DeleteReusedPreparedAsync(_deleteIds));
    [BenchmarkCategory("Delete"), Benchmark]
    public Task<int> Native_DbBatchDelete() => RequireAsync(RequireBatchSupport(_runner.DeleteDbBatchAsync, _deleteIds));
    [BenchmarkCategory("Delete"), Benchmark, PrecomputedTransportBenchmark]
    public Task<int> Raw_PrecomputedTvpDeleteFloor() => RequireAsync(_runner.DeleteTvpAsync(_deleteTvp));
    [BenchmarkCategory("Delete"), Benchmark]
    public async Task<int> Raw_EndToEndTvpDelete()
    {
        using var tvp = BuildDeleteTvp(_deleteIds);
        return await RequireAsync(_runner.DeleteTvpAsync(tvp)).ConfigureAwait(false);
    }

    private async Task<int> RequireAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows ? affected : throw new InvalidOperationException($"Expected {Rows} affected rows, but received {affected}.");
    }

    private Task<int> RequireBatchSupport<T>(Func<IReadOnlyList<T>, Task<int>> execution, IReadOnlyList<T> values)
        => _runner.CanCreateBatch
            ? execution(values)
            : throw new NotSupportedException("Microsoft.Data.SqlClient reports SqlConnection.CanCreateBatch = false; only Native_DbBatch benchmarks are unavailable.");

    private static BatchMutationBenchmarkItem[] CreateItems(int count, int firstId, string prefix)
    {
        var items = new BatchMutationBenchmarkItem[count];
        for (var i = 0; i < count; i++)
            items[i] = new BatchMutationBenchmarkItem { Id = firstId + i, ValueText = $"{prefix} {i}" };
        return items;
    }

    private static string BuildInsertSql(int count)
    {
        var sql = new StringBuilder("INSERT INTO InquiryBatchEvidence (Id, ValueText) VALUES ");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sql.Append(',');
            sql.Append("(@id").Append(i).Append(",@value").Append(i).Append(')');
        }
        return sql.Append(';').ToString();
    }

    private static DataTable BuildDeleteTvp(IEnumerable<int> ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        foreach (var id in ids) table.Rows.Add(id);
        return table;
    }
}
