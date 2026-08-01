using BenchmarkDotNet.Running;
using Inquiry.Benchmarks.Contracts;

namespace Inquiry.Benchmarks.SqlServer;

public static class Program
{
    public static async Task<int> Main(string[] args)
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
                8 => "net8-live-v1",
                10 => "net10-live-v1",
                _ => throw new InvalidOperationException("Authoritative collection benchmarks require net8.0 or net10.0."),
            });
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(
                ["--filter", "*SqlServerCollectionBenchmarks*"], BenchmarkDotNetConfigFactory.Create(job));
            return 0;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
