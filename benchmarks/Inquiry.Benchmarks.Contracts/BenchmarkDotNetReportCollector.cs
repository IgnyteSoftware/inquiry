using BenchmarkDotNet.Reports;
using Inquiry.Benchmarks.Contracts.Evidence;

namespace Inquiry.Benchmarks.Contracts;

public sealed record BenchmarkDotNetReportSnapshot(
    BenchmarkDotNetGcStats GcStats,
    IReadOnlyList<BenchmarkDotNetMeasurement> Measurements,
    IReadOnlyList<BenchmarkDotNetField> ResultFields);

/// <summary>Reads only public BenchmarkDotNet 0.15.8 report APIs; no allocation value is reconstructed.</summary>
public static class BenchmarkDotNetReportCollector
{
    public static BenchmarkDotNetReportSnapshot Collect(BenchmarkReport report, bool memoryDiagnoserEnabled)
    {
        ArgumentNullException.ThrowIfNull(report);
        var resultRuns = report.GetResultRuns()
            .OrderBy(static measurement => measurement.LaunchIndex)
            .ThenBy(static measurement => measurement.IterationIndex)
            .ToArray();
        ValidateNativeCoordinates(
            resultRuns.Select(static measurement => (measurement.LaunchIndex, measurement.IterationIndex)).ToArray(),
            report.BenchmarkCase.Job.Run.LaunchCount,
            report.BenchmarkCase.Job.Run.IterationCount);
        var measurements = resultRuns
            .Select(measurement => new BenchmarkDotNetMeasurement(
                measurement.LaunchIndex - 1,
                measurement.IterationIndex - 1,
                measurement.Operations,
                measurement.GetAverageTime().Nanoseconds))
            .ToArray();
        if (measurements.Length == 0)
            throw new InvalidDataException("BenchmarkDotNet report contains no target measurements.");

        var bytesPerOperation = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
        var gcStats = new BenchmarkDotNetGcStats(
            report.GcStats.TotalOperations,
            bytesPerOperation,
            BenchmarkAggregationCatalog.GcStatsProvenance,
            memoryDiagnoserEnabled);
        var mean = BenchmarkStatistics.Mean(measurements.Select(static measurement => measurement.NanosecondsPerOperation));
        var median = BenchmarkStatistics.Median(measurements.Select(static measurement => measurement.NanosecondsPerOperation));
        var allocated = bytesPerOperation is null
            ? BenchmarkAggregationCatalog.UnavailableAllocation
            : BenchmarkStatistics.FormatBenchmarkDotNet(bytesPerOperation.Value,
                BenchmarkAggregationCatalog.Required.BenchmarkDotNetAllocationUnit);
        return new BenchmarkDotNetReportSnapshot(
            gcStats,
            measurements,
            [
                new("Mean", BenchmarkStatistics.FormatBenchmarkDotNet(mean,
                    BenchmarkAggregationCatalog.Required.BenchmarkDotNetTimingUnit)),
                new("Median", BenchmarkStatistics.FormatBenchmarkDotNet(median,
                    BenchmarkAggregationCatalog.Required.BenchmarkDotNetTimingUnit)),
                new("Allocated", allocated),
            ]);
    }

    internal static void ValidateNativeCoordinates(
        IReadOnlyList<(int LaunchIndex, int IterationIndex)> coordinates,
        int expectedLaunchCount,
        int expectedIterationCount)
    {
        if (expectedLaunchCount <= 0 || expectedIterationCount <= 0)
            throw new InvalidDataException("BenchmarkDotNet report has invalid configured launch or iteration counts.");

        var expected = Enumerable.Range(1, expectedLaunchCount)
            .SelectMany(launch => Enumerable.Range(1, expectedIterationCount)
                .Select(iteration => (LaunchIndex: launch, IterationIndex: iteration)))
            .ToArray();
        var actual = coordinates.OrderBy(static coordinate => coordinate.LaunchIndex)
            .ThenBy(static coordinate => coordinate.IterationIndex)
            .ToArray();
        if (!actual.SequenceEqual(expected))
            throw new InvalidDataException(
                "BenchmarkDotNet target measurements must contain the exact contiguous one-based launch/iteration coordinate set configured by the job.");
    }
}
