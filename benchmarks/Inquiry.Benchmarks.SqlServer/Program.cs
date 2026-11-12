using BenchmarkDotNet.Running;
using Inquiry.Benchmarks.Contracts;

namespace Inquiry.Benchmarks.SqlServer;

public static class Program
{
    public static Task<int> Main(string[] args) => args switch
    {
        [ProcessColdStart.ProbeSwitch, var leg] => ProcessColdStart.ProbeAsync(leg),
        [ProcessColdStart.DriverSwitch, .. var rest] => ProcessColdStart.RunAsync(rest),
        _ => RunBenchmarksAsync(args),
    };

    // Kept out of the entry point, so that a cold-start probe does not load BenchmarkDotNet.
    private static async Task<int> RunBenchmarksAsync(string[] args)
    {
        if (args is ["--collection-verify"])
        {
            await using var database = await SqlServerCollectionBenchmarkDatabase.CreateAsync().ConfigureAwait(false);
            await SqlServerCollectionCorrectness.VerifyAsync(database).ConfigureAwait(false);
            Console.WriteLine("SQLSERVER-COLLECTION-VERIFY-OK");
            return 0;
        }
        if (args is ["--collection-evidence", var output])
        {
            await SqlServerCollectionEvidenceCollector.CollectAndWriteAsync(output).ConfigureAwait(false);
            Console.WriteLine("SQLSERVER-COLLECTION-EVIDENCE-OK");
            return 0;
        }
        if (args is ["--collection-smoke"])
        {
            await using var database = await SqlServerCollectionBenchmarkDatabase.CreateAsync().ConfigureAwait(false);
            await SqlServerCollectionCorrectness.VerifyAsync(database).ConfigureAwait(false);
            Console.WriteLine("SQLSERVER-COLLECTION-SMOKE-OK (non-authoritative)");
            return 0;
        }
        if (args is ["--collection-benchmark"])
        {
            var job = BenchmarkJobCatalog.GetRequired(Environment.Version.Major switch
            {
                10 => "net10-live-v1",
                11 => "net11-live-v1",
                _ => throw new InvalidOperationException("Authoritative collection benchmarks require net10.0 or net11.0."),
            });
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(
                ["--filter", "*SqlServerCollectionBenchmarks*"], BenchmarkDotNetConfigFactory.Create(job));
            return 0;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
