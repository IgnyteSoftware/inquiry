using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Inquiry.Pipeline;

namespace Inquiry.Benchmarks;

/// <summary>
/// The cancellation race helper alone (#507), against awaiting the same provider task directly, with one
/// never-cancelled caller token. <c>Completed</c> is the shape Microsoft.Data.Sqlite returns. <c>Pending</c> is
/// the shape of a networked driver: the helper attaches while the task is still running, so it pays for its
/// registration and continuation. The provider task is completed inline, so the bytes are deterministic.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet disposes these fields in the [GlobalCleanup] method.")]
public class CancellationHelperBenchmarks
{
    private readonly object _scalarValue = "Shipper 0";
    private readonly int _intValue = 42;
    private Task<object?> _completedObject = null!;
    private CancellationTokenSource _source = null!;
    private CancellationToken _token;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _completedObject = Task.FromResult<object?>(_scalarValue);
        _source = new CancellationTokenSource();
        _token = _source.Token;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _source.Dispose();

    [BenchmarkCategory("CompletedObject"), Benchmark(Baseline = true)]
    public async Task<object?> CompletedObject_Direct()
        => await _completedObject;

    [BenchmarkCategory("CompletedObject"), Benchmark]
    public async Task<object?> CompletedObject_Helper()
        => await InquiryCancellation.AwaitEnforcingCallerToken(_completedObject, _token);

    [BenchmarkCategory("PendingInt"), Benchmark(Baseline = true)]
    public async Task<int> PendingInt_Direct()
    {
        var provider = new TaskCompletionSource<int>();
        var pending = provider.Task;
        provider.SetResult(_intValue);
        return await pending;
    }

    [BenchmarkCategory("PendingInt"), Benchmark]
    public async Task<int> PendingInt_Helper()
    {
        var provider = new TaskCompletionSource<int>();
        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, _token);
        provider.SetResult(_intValue);
        return await pending;
    }

    [BenchmarkCategory("PendingObject"), Benchmark(Baseline = true)]
    public async Task<object?> PendingObject_Direct()
    {
        var provider = new TaskCompletionSource<object?>();
        var pending = provider.Task;
        provider.SetResult(_scalarValue);
        return await pending;
    }

    [BenchmarkCategory("PendingObject"), Benchmark]
    public async Task<object?> PendingObject_Helper()
    {
        var provider = new TaskCompletionSource<object?>();
        var pending = InquiryCancellation.AwaitEnforcingCallerToken(provider.Task, _token);
        provider.SetResult(_scalarValue);
        return await pending;
    }
}
