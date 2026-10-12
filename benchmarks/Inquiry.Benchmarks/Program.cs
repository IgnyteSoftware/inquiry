using BenchmarkDotNet.Running;
using Inquiry.Benchmarks;

if (args.Contains("--capture-plans", StringComparer.OrdinalIgnoreCase))
{
    await QueryPlanCapture.RunAsync();
    return 0;
}

// Default: run every benchmark. Pass `--filter *Customer*` etc. to scope a run.
// Examples:
//   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks
//   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks -- --filter *SelectAll*
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// BenchmarkDotNet exits 0 even when a benchmark or its setup fails, which would hide the failure from CI.
return summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success)) ? 1 : 0;

internal partial class Program { }
