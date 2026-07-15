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
    private BatchMutationBenchmarkStore _store = null!;
    private MySqlBatchMutationStrategyRunner _runner = null!;
    private BatchMutationBenchmarkItem[] _selectedInsertItems = null!;
    private BatchMutationBenchmarkItem[] _selectedUpdateItems = null!;
    private MySqlBatchMutationItem[] _rawInsertItems = null!;
    private MySqlBatchMutationItem[] _rawUpdateItems = null!;
    private int[] _deleteIds = null!;

    [Params(1, 10, 100, 1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _database = MariaDbBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _store = _database.BatchMutations;
        _runner = new MySqlBatchMutationStrategyRunner(_database.ConnectionString);
        _runner.InitializeAsync().GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _runner.ResetAsync(Rows).GetAwaiter().GetResult();
        _selectedInsertItems = CreateSelectedItems(Rows, 100_001, "Inserted");
        _selectedUpdateItems = CreateSelectedItems(Rows, 1, "Updated");
        _rawInsertItems = CreateRawItems(Rows, 100_001, "Inserted");
        _rawUpdateItems = CreateRawItems(Rows, 1, "Updated");
        _deleteIds = CreateIds(Rows, 1);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedInsertAll() => RequireAffectedRowsAsync(_store.InsertAllAsync(_selectedInsertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Direct_ReusedPreparedInsert() => RequireAffectedRowsAsync(_runner.InsertReusedCommandAsync(_rawInsertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Native_DbBatchInsert() => RequireAffectedRowsAsync(_runner.InsertDbBatchAsync(_rawInsertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Raw_MultiRowInsertControl() => RequireAffectedRowsAsync(_runner.InsertSetBasedAsync(_rawInsertItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedUpdateAll() => RequireAffectedRowsAsync(_store.UpdateAllAsync(_selectedUpdateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Direct_ReusedPreparedUpdate() => RequireAffectedRowsAsync(_runner.UpdateReusedCommandAsync(_rawUpdateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Native_DbBatchUpdate() => RequireAffectedRowsAsync(_runner.UpdateDbBatchAsync(_rawUpdateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Raw_CaseUpdateControl() => RequireAffectedRowsAsync(_runner.UpdateSetBasedAsync(_rawUpdateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Raw_DerivedTableJoinControl() => RequireAffectedRowsAsync(_runner.UpdateDerivedTableJoinAsync(_rawUpdateItems));

    [BenchmarkCategory("BatchDelete"), Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedDeleteAll() => RequireAffectedRowsAsync(_store.DeleteAllAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Direct_ReusedPreparedDelete() => RequireAffectedRowsAsync(_runner.DeleteReusedCommandAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Native_DbBatchDelete() => RequireAffectedRowsAsync(_runner.DeleteDbBatchAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Raw_ExpandedInDeleteControl() => RequireAffectedRowsAsync(_runner.DeleteSetBasedAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Raw_JsonTableDeleteControl() => RequireAffectedRowsAsync(_runner.DeleteJsonTableAsync(_deleteIds));

    private static MySqlBatchMutationItem[] CreateRawItems(int count, int firstId, string valuePrefix)
    {
        var items = new MySqlBatchMutationItem[count];
        for (var i = 0; i < count; i++) items[i] = new(firstId + i, $"{valuePrefix} {i}");
        return items;
    }

    private static BatchMutationBenchmarkItem[] CreateSelectedItems(int count, int firstId, string valuePrefix)
    {
        var items = new BatchMutationBenchmarkItem[count];
        for (var i = 0; i < count; i++)
            items[i] = new BatchMutationBenchmarkItem { Id = firstId + i, ValueText = $"{valuePrefix} {i}" };
        return items;
    }

    private static int[] CreateIds(int count, int firstId)
    {
        var ids = new int[count];
        for (var i = 0; i < count; i++) ids[i] = firstId + i;
        return ids;
    }

    private async Task<int> RequireAffectedRowsAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows
            ? affected
            : throw new InvalidOperationException($"Expected {Rows} affected rows, but the provider returned {affected}.");
    }
}
