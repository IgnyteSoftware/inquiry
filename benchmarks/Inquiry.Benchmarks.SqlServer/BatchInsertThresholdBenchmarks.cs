using BenchmarkDotNet.Attributes;

namespace Inquiry.Benchmarks.SqlServer;

[MemoryDiagnoser]
[InvocationCount(1)]
public class BatchInsertThresholdBenchmarks
{
    private readonly BatchMutationStrategyBenchmarks _benchmark = new();

    [Params(249, 250, 251)]
    public int Rows;

    [GlobalSetup]
    public void Setup()
    {
        _benchmark.Rows = Rows;
        _benchmark.GlobalSetup();
    }

    [IterationSetup]
    public void Reset() => _benchmark.IterationSetup();

    [GlobalCleanup]
    public void Cleanup() => _benchmark.GlobalCleanup();

    [Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedInsertAll() => _benchmark.Inquiry_SelectedInsertAll();

    [Benchmark]
    public Task<int> Direct_ReusedPreparedInsert() => _benchmark.Direct_ReusedPreparedInsert();

    [Benchmark]
    public Task<int> Native_DbBatchInsert() => _benchmark.Native_DbBatchInsert();

    [Benchmark]
    public Task<int> Raw_EndToEndMultiRowInsert() => _benchmark.Raw_EndToEndMultiRowInsert();
}
