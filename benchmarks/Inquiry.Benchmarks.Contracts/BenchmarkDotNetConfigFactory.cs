using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;
using System.Reflection;

namespace Inquiry.Benchmarks.Contracts;

/// <summary>Materializes the checked job contract into the executable BenchmarkDotNet configuration.</summary>
public static class BenchmarkDotNetConfigFactory
{
    public static ManualConfig Create(BenchmarkJobContract contract)
    {
        var actualVersion = typeof(Job).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (actualVersion is null || !actualVersion.StartsWith(BenchmarkJobCatalog.BenchmarkDotNetVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Loaded BenchmarkDotNet '{actualVersion}' does not match the checked version '{BenchmarkJobCatalog.BenchmarkDotNetVersion}'.");
        if (!contract.FullJsonExport)
            throw new ArgumentException("Checked release jobs require the full JSON exporter.", nameof(contract));
        if (!contract.MemoryDiagnoser)
            throw new ArgumentException("Checked release jobs require BenchmarkDotNet's MemoryDiagnoser.", nameof(contract));
        var runtime = contract.RuntimeTfm switch
        {
            "net8.0" => CoreRuntime.Core80,
            "net10.0" => CoreRuntime.Core10_0,
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract.RuntimeTfm, "Unsupported checked runtime."),
        };
        var outlierMode = contract.OutlierMode switch
        {
            "dont-remove" => OutlierMode.DontRemove,
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract.OutlierMode, "Unsupported checked outlier policy."),
        };

        var job = Job.Default
            .WithId(contract.Id)
            .WithRuntime(runtime)
            .WithLaunchCount(contract.LaunchCount)
            .WithWarmupCount(contract.WarmupIterationFloor)
            .WithIterationCount(contract.MeasurementIterationFloor)
            .WithInvocationCount(contract.InvocationCount)
            .WithUnrollFactor(contract.UnrollFactor)
            .WithMinIterationTime(TimeInterval.Millisecond * contract.MinIterationTimeMilliseconds)
            .WithMaxRelativeError(contract.MaxRelativeError)
            .WithEvaluateOverhead(contract.EvaluateOverhead)
            .WithOutlierMode(outlierMode);

        return ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(contract.ArtifactRoot);
    }
}
