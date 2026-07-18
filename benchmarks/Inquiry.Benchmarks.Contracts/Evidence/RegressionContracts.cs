using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inquiry.Benchmarks.Contracts.Evidence;

public sealed record RegressionBaseline(
    string SchemaVersion,
    string Provider,
    string RuntimeTfm,
    string GeneratedAtUtc,
    string Commit,
    string Environment,
    IReadOnlyList<RegressionBaselineCase> Cases)
{
    public const string CurrentSchemaVersion = "regression-baseline-v1";
}

public sealed record RegressionBaselineCase(
    string FullName,
    double MedianNs,
    double MeanNs,
    long? AllocatedBytes,
    double RelativeBudget,
    double AbsoluteBudgetNs,
    double? AllocationRelativeBudget);

public sealed record RegressionResult(
    string FullName,
    RegressionVerdict LatencyVerdict,
    RegressionVerdict AllocationVerdict,
    double BaselineMedianNs,
    double CurrentMedianNs,
    double LatencyDeltaPercent,
    double LatencyDeltaNs,
    long? BaselineAllocatedBytes,
    long? CurrentAllocatedBytes,
    double? AllocationDeltaPercent);

public enum RegressionVerdict { Pass, Fail, Skip }

public static class RegressionComparator
{
    public static IReadOnlyList<RegressionResult> Compare(
        RegressionBaseline baseline,
        IReadOnlyList<BdnBenchmarkCase> currentResults)
    {
        var currentByName = new Dictionary<string, BdnBenchmarkCase>(StringComparer.Ordinal);
        foreach (var c in currentResults)
        {
            if (c.FullName is not null)
                currentByName[c.FullName] = c;
        }

        var results = new List<RegressionResult>();
        foreach (var expected in baseline.Cases)
        {
            if (!currentByName.TryGetValue(expected.FullName, out var current) ||
                current.Statistics is null)
            {
                results.Add(new RegressionResult(
                    expected.FullName,
                    RegressionVerdict.Skip,
                    RegressionVerdict.Skip,
                    expected.MedianNs, 0, 0, 0,
                    expected.AllocatedBytes, null, null));
                continue;
            }

            var currentMedian = current.Statistics.Median;
            var deltaNs = currentMedian - expected.MedianNs;
            var deltaPercent = expected.MedianNs > 0
                ? (deltaNs / expected.MedianNs) * 100.0
                : 0.0;

            var latencyVerdict = RegressionVerdict.Pass;
            if (deltaNs > expected.AbsoluteBudgetNs &&
                deltaPercent > expected.RelativeBudget * 100.0)
            {
                latencyVerdict = RegressionVerdict.Fail;
            }

            var allocVerdict = RegressionVerdict.Skip;
            double? allocDeltaPercent = null;
            if (expected.AllocatedBytes is not null &&
                current.Memory?.BytesAllocatedPerOperation is not null)
            {
                var allocDelta = current.Memory.BytesAllocatedPerOperation.Value - expected.AllocatedBytes.Value;
                allocDeltaPercent = expected.AllocatedBytes.Value > 0
                    ? ((double)allocDelta / expected.AllocatedBytes.Value) * 100.0
                    : 0.0;

                var allocBudget = expected.AllocationRelativeBudget ?? 0.05;
                allocVerdict = allocDeltaPercent > allocBudget * 100.0
                    ? RegressionVerdict.Fail
                    : RegressionVerdict.Pass;
            }

            results.Add(new RegressionResult(
                expected.FullName,
                latencyVerdict,
                allocVerdict,
                expected.MedianNs,
                currentMedian,
                Math.Round(deltaPercent, 2),
                Math.Round(deltaNs, 2),
                expected.AllocatedBytes,
                current.Memory?.BytesAllocatedPerOperation,
                allocDeltaPercent is not null ? Math.Round(allocDeltaPercent.Value, 2) : null));
        }

        return results;
    }
}

public sealed class BdnReport
{
    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("Benchmarks")]
    public List<BdnBenchmarkCase>? Benchmarks { get; set; }
}

public sealed class BdnBenchmarkCase
{
    [JsonPropertyName("FullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Method")]
    public string? Method { get; set; }

    [JsonPropertyName("Parameters")]
    public string? Parameters { get; set; }

    [JsonPropertyName("Statistics")]
    public BdnStatistics? Statistics { get; set; }

    [JsonPropertyName("Memory")]
    public BdnMemory? Memory { get; set; }
}

public sealed class BdnStatistics
{
    [JsonPropertyName("N")]
    public int N { get; set; }

    [JsonPropertyName("Min")]
    public double Min { get; set; }

    [JsonPropertyName("Max")]
    public double Max { get; set; }

    [JsonPropertyName("Mean")]
    public double Mean { get; set; }

    [JsonPropertyName("Median")]
    public double Median { get; set; }

    [JsonPropertyName("StandardDeviation")]
    public double StandardDeviation { get; set; }

    [JsonPropertyName("StandardError")]
    public double StandardError { get; set; }
}

public sealed class BdnMemory
{
    [JsonPropertyName("Gen0Collections")]
    public int Gen0Collections { get; set; }

    [JsonPropertyName("Gen1Collections")]
    public int Gen1Collections { get; set; }

    [JsonPropertyName("Gen2Collections")]
    public int Gen2Collections { get; set; }

    [JsonPropertyName("TotalOperations")]
    public long TotalOperations { get; set; }

    [JsonPropertyName("BytesAllocatedPerOperation")]
    public long? BytesAllocatedPerOperation { get; set; }
}

public static class RegressionBaselineGenerator
{
    public static RegressionBaseline Generate(
        BdnReport report,
        string provider,
        string runtimeTfm,
        string commit,
        string environment,
        double defaultRelativeBudget = 0.10,
        double defaultAbsoluteBudgetNs = 5000.0,
        double defaultAllocationRelativeBudget = 0.05)
    {
        var cases = new List<RegressionBaselineCase>();
        foreach (var benchmark in report.Benchmarks ?? [])
        {
            if (benchmark.FullName is null || benchmark.Statistics is null)
                continue;

            cases.Add(new RegressionBaselineCase(
                benchmark.FullName,
                Math.Round(benchmark.Statistics.Median, 4),
                Math.Round(benchmark.Statistics.Mean, 4),
                benchmark.Memory?.BytesAllocatedPerOperation,
                defaultRelativeBudget,
                defaultAbsoluteBudgetNs,
                defaultAllocationRelativeBudget));
        }

        return new RegressionBaseline(
            RegressionBaseline.CurrentSchemaVersion,
            provider,
            runtimeTfm,
            DateTimeOffset.UtcNow.ToString("o"),
            commit,
            environment,
            cases);
    }
}
