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
    private OracleBatchMutationItem[] _insertItems = null!;
    private int[] _insertIds = null!;
    private string[] _insertValues = null!;
    private int[] _insertValueSizes = null!;
    private OracleBatchMutationItem[] _updateItems = null!;
    private int[] _updateIds = null!;
    private string[] _updateValues = null!;
    private int[] _updateValueSizes = null!;
    private int[] _deleteIds = null!;

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
    public void IterationSetup()
    {
        _runner.ResetAsync(Rows).GetAwaiter().GetResult();
        _insertItems = CreateItems(Rows, 100_001, "Inserted");
        (_insertIds, _insertValues, _insertValueSizes) = CreateArrayBindingInputs(_insertItems);
        _updateItems = CreateItems(Rows, 1, "Updated");
        (_updateIds, _updateValues, _updateValueSizes) = CreateArrayBindingInputs(_updateItems);
        _deleteIds = CreateIds(Rows, 1);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("BatchInsert"), Benchmark(Baseline = true)]
    public Task<int> Insert_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.InsertReusedCommandAsync(_insertItems));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_ArrayBinding() => RequireAffectedRowsAsync(
        _runner.InsertArrayBindingAsync(_insertItems, _insertIds, _insertValues, _insertValueSizes));

    [BenchmarkCategory("BatchInsert"), Benchmark]
    public Task<int> Insert_ProductionInsertSelect() => RequireAffectedRowsAsync(_runner.InsertProductionInsertSelectAsync(_insertItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark(Baseline = true)]
    public Task<int> Update_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.UpdateReusedCommandAsync(_updateItems));

    [BenchmarkCategory("BatchUpdate"), Benchmark]
    public Task<int> Update_ArrayBinding() => RequireAffectedRowsAsync(
        _runner.UpdateArrayBindingAsync(_updateItems, _updateIds, _updateValues, _updateValueSizes));

    [BenchmarkCategory("BatchDelete"), Benchmark(Baseline = true)]
    public Task<int> Delete_ReusedPreparedCommand() => RequireAffectedRowsAsync(_runner.DeleteReusedCommandAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_ArrayBinding() => RequireAffectedRowsAsync(_runner.DeleteArrayBindingAsync(_deleteIds));

    [BenchmarkCategory("BatchDelete"), Benchmark]
    public Task<int> Delete_ProductionJsonTable() => RequireAffectedRowsAsync(_runner.DeleteJsonTableAsync(_deleteIds));

    private static OracleBatchMutationItem[] CreateItems(int count, int firstId, string valuePrefix)
    {
        var items = new OracleBatchMutationItem[count];
        for (var i = 0; i < count; i++) items[i] = new(firstId + i, $"{valuePrefix} {i}");
        return items;
    }

    private static int[] CreateIds(int count, int firstId)
    {
        var ids = new int[count];
        for (var i = 0; i < count; i++) ids[i] = firstId + i;
        return ids;
    }

    private static (int[] Ids, string[] Values, int[] ValueSizes) CreateArrayBindingInputs(
        IReadOnlyList<OracleBatchMutationItem> items)
    {
        var ids = new int[items.Count];
        var values = new string[items.Count];
        var sizes = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            ids[i] = items[i].Id;
            values[i] = items[i].Value;
            sizes[i] = 100;
        }

        return (ids, values, sizes);
    }

    private async Task<int> RequireAffectedRowsAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows
            ? affected
            : throw new InvalidOperationException($"Expected {Rows} affected rows, but the provider returned {affected}.");
    }
}
