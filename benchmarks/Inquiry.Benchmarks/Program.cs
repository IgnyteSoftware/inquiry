using System.Runtime.CompilerServices;
using BenchmarkDotNet.Running;
using Inquiry.Benchmarks;

if (args is [ProcessColdStart.ProbeSwitch, var leg])
{
    return await ProcessColdStart.ProbeAsync(leg);
}

if (args is [ProcessColdStart.DriverSwitch, .. var rest])
{
    return await ProcessColdStart.RunAsync(rest);
}

if (args.Contains("--capture-plans", StringComparer.OrdinalIgnoreCase))
{
    await QueryPlanCapture.RunAsync();
    return 0;
}

return RunBenchmarks(args);

// Kept out of the entry point, so that a cold-start probe does not load BenchmarkDotNet.
[MethodImpl(MethodImplOptions.NoInlining)]
static int RunBenchmarks(string[] args)
{
    // Default: run every benchmark. Pass `--filter *Customer*` etc. to scope a run.
    // Examples:
    //   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks
    //   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks -- --filter *SelectAll*
    var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

    // BenchmarkDotNet exits 0 even when a benchmark or its setup fails, which would hide the failure from CI.
    return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;
}

internal static partial class Program { }
