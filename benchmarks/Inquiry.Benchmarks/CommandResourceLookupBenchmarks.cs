using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Commands;
using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;

namespace Inquiry.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class CommandResourceLookupBenchmarks
{
    private readonly ConditionalWeakTable<SqliteCommand, LegacyResourceSet> _legacyResources = new();
    private readonly SqliteCommand _resourceCommand = new();
    private readonly NoOpResource _resource = new();
    private SqliteCommand _noResourceCommand = null!;
    private SqliteCommand _signalResourceCommand = null!;

    [GlobalSetup]
    public void Setup()
    {
        _signalResourceCommand = new SqliteCommand();
        InquiryCommandResources.Register(_signalResourceCommand, _resource);
        _legacyResources.GetOrCreateValue(_signalResourceCommand).Add(_resource);

        do
        {
            _noResourceCommand?.Dispose();
            _noResourceCommand = new SqliteCommand();
        }
        while (InquiryCommandResources.HasResourceSignal(_noResourceCommand));
    }

    [BenchmarkCategory("NoResource"), Benchmark(Baseline = true)]
    public void TableProbeWithoutResource()
        => LegacyDispose(_noResourceCommand);

    [BenchmarkCategory("NoResource"), Benchmark]
    public void SignalWithoutResource()
        => InquiryCommandResources.Dispose(_noResourceCommand);

    [BenchmarkCategory("OneResource"), Benchmark(Baseline = true)]
    public void TableProbeWithResource()
    {
        _legacyResources.GetOrCreateValue(_resourceCommand).Add(_resource);
        LegacyDispose(_resourceCommand);
    }

    [BenchmarkCategory("OneResource"), Benchmark]
    public void SignalWithResource()
    {
        InquiryCommandResources.Register(_resourceCommand, _resource);
        InquiryCommandResources.Dispose(_resourceCommand);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        InquiryCommandResources.Dispose(_signalResourceCommand);
        LegacyDispose(_signalResourceCommand);
        _noResourceCommand.Dispose();
        _signalResourceCommand.Dispose();
        _resourceCommand.Dispose();
    }

    private void LegacyDispose(SqliteCommand command)
    {
        if (!_legacyResources.TryGetValue(command, out var set)) return;
        _legacyResources.Remove(command);
        set.Dispose();
    }

    private sealed class NoOpResource : IInquiryExecutionResource
    {
        public void Dispose() { }
    }

    private sealed class LegacyResourceSet : IDisposable
    {
        private readonly object _gate = new();
        private List<IInquiryExecutionResource>? _items = new();

        public void Add(IInquiryExecutionResource resource)
        {
            lock (_gate)
            {
                if (_items is null) throw new ObjectDisposedException(nameof(LegacyResourceSet));
                _items.Add(resource);
            }
        }

        public void Dispose()
        {
            List<IInquiryExecutionResource>? items;
            lock (_gate)
            {
                items = _items;
                _items = null;
            }
            if (items is null) return;
            List<Exception>? exceptions = null;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                try
                {
                    items[i].Dispose();
                }
                catch (Exception exception)
                {
                    exceptions = InquiryCleanup.Add(exceptions, exception);
                }
            }
            InquiryCleanup.ThrowIfAny(exceptions);
        }
    }
}
