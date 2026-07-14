using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Benchmarks.MySqlFamily;

namespace Inquiry.Benchmarks.MySql;

[MemoryDiagnoser]
[InvocationCount(1)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BatchMutationStrategyBenchmarks
{
    private MySqlBenchmarkDatabase _database = null!;
    private MySqlBatchMutationStrategyRunner _runner = null!;

    [Params(1, 10, 100, 1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _database = MySqlBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _runner = new MySqlBatchMutationStrategyRunner(_database.ConnectionString);
        _runner.InitializeAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup() => _runner.ResetAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public Task<int> Insert_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.InsertReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_DbBatch() => RequireAffectedRowsAsync(_runner.InsertDbBatchAsync(Rows));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_SetBasedSql() => RequireAffectedRowsAsync(_runner.InsertSetBasedAsync(Rows));

    [BenchmarkCategory("BatchUpdate"), Benchmark(Baseline = true)]
    public Task<int> Update_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.UpdateReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_DbBatch() => RequireAffectedRowsAsync(_runner.UpdateDbBatchAsync(Rows));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_SetBasedSql() => RequireAffectedRowsAsync(_runner.UpdateSetBasedAsync(Rows));

    [BenchmarkCategory("BatchDelete"), Benchmark(Baseline = true)]
    public Task<int> Delete_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.DeleteReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_DbBatch() => RequireAffectedRowsAsync(_runner.DeleteDbBatchAsync(Rows));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_SetBasedSql() => RequireAffectedRowsAsync(_runner.DeleteSetBasedAsync(Rows));

    private async Task<int> RequireAffectedRowsAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows
            ? affected
            : throw new InvalidOperationException($"Expected {Rows} affected rows, but the provider returned {affected}.");
    }
}
