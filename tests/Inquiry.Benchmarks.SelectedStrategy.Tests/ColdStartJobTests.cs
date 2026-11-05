using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

public sealed class ColdStartJobTests
{
    [Fact]
    public void EveryCaseMeasuresOneFirstCallPerFreshProcess()
    {
        var runInfo = BenchmarkConverter.TypeToBenchmarks(typeof(global::Inquiry.Benchmarks.ColdStartBenchmarks));

        Assert.NotEmpty(runInfo.BenchmarksCases);
        Assert.All(runInfo.BenchmarksCases, static benchmarkCase =>
        {
            var job = benchmarkCase.Job;
            Assert.Equal(RunStrategy.ColdStart, job.Run.RunStrategy);
            Assert.Equal(10, job.Run.LaunchCount);
            Assert.Equal(0, job.Run.WarmupCount);
            Assert.Equal(1, job.Run.IterationCount);
            Assert.Equal(1, job.Run.InvocationCount);
        });

        // MemoryDiagnoser measures an extra call after the measured one, so it cannot report first-call allocation.
        Assert.DoesNotContain(runInfo.Config.GetDiagnosers(), static diagnoser => diagnoser is MemoryDiagnoser);
    }
}
