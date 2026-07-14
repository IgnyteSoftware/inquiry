using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Benchmarks.MySqlFamily;

namespace Inquiry.Benchmarks.MariaDb;

[MemoryDiagnoser]
[InvocationCount(1)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BatchMutationStrategyBenchmarks
{
    private MariaDbBenchmarkDatabase _database = null!;
    private MySqlBatchMutationStrategyRunner _runner = null!;
    private MySqlBatchMutationItem[] _insertItems = null!;
    private MySqlBatchMutationItem[] _updateItems = null!;
    private MySqlBatchMutationItem[] _deleteItems = null!;

    [Params(1, 10, 100, 1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _database = MariaDbBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _runner = new MySqlBatchMutationStrategyRunner(_database.ConnectionString);
        _runner.InitializeAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _runner.ResetAsync(Rows).GetAwaiter().GetResult();
        _insertItems = CreateItems(Rows, 100_001, "Inserted");
        _updateItems = CreateItems(Rows, 1, "Updated");
        _deleteItems = CreateItems(Rows, 1, "Deleted");
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public Task<int> Insert_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.InsertReusedCommandAsync(_insertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_DbBatch() => RequireAffectedRowsAsync(_runner.InsertDbBatchAsync(_insertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_SetBasedSql() => RequireAffectedRowsAsync(_runner.InsertSetBasedAsync(_insertItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark(Baseline = true)]
    public Task<int> Update_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.UpdateReusedCommandAsync(_updateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_DbBatch() => RequireAffectedRowsAsync(_runner.UpdateDbBatchAsync(_updateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_SetBasedSql() => RequireAffectedRowsAsync(_runner.UpdateSetBasedAsync(_updateItems));

    [BenchmarkCategory("BatchDelete"), Benchmark(Baseline = true)]
    public Task<int> Delete_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.DeleteReusedCommandAsync(_deleteItems));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_DbBatch() => RequireAffectedRowsAsync(_runner.DeleteDbBatchAsync(_deleteItems));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_SetBasedSql() => RequireAffectedRowsAsync(_runner.DeleteSetBasedAsync(_deleteItems));

    private static MySqlBatchMutationItem[] CreateItems(int count, int firstId, string valuePrefix)
    {
        var items = new MySqlBatchMutationItem[count];
        for (var i = 0; i < count; i++) items[i] = new(firstId + i, $"{valuePrefix} {i}");
        return items;
    }

    private async Task<int> RequireAffectedRowsAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows
            ? affected
            : throw new InvalidOperationException($"Expected {Rows} affected rows, but the provider returned {affected}.");
    }
}
