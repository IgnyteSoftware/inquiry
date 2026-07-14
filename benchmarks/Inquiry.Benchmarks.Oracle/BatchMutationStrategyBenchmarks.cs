using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Inquiry.Benchmarks.Oracle;

[MemoryDiagnoser]
[InvocationCount(1)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class BatchMutationStrategyBenchmarks
{
    private OracleBenchmarkDatabase _database = null!;
    private OracleBatchMutationStrategyRunner _runner = null!;

    [Params(1, 10, 100, 1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _database = OracleBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _runner = new OracleBatchMutationStrategyRunner(_database.ConnectionString);
        _runner.InitializeAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup() => _runner.ResetAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public Task<int> Insert_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.InsertReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_ArrayBinding() => RequireAffectedRowsAsync(_runner.InsertArrayBindingAsync(Rows));

    [BenchmarkCategory("BatchUpdate"), Benchmark(Baseline = true)]
    public Task<int> Update_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.UpdateReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_ArrayBinding() => RequireAffectedRowsAsync(_runner.UpdateArrayBindingAsync(Rows));

    [BenchmarkCategory("BatchDelete"), Benchmark(Baseline = true)]
    public Task<int> Delete_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.DeleteReusedCommandAsync(Rows));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_ArrayBinding() => RequireAffectedRowsAsync(_runner.DeleteArrayBindingAsync(Rows));

    private async Task<int> RequireAffectedRowsAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows
            ? affected
            : throw new InvalidOperationException($"Expected {Rows} affected rows, but the provider returned {affected}.");
    }
}
