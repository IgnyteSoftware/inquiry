using BenchmarkDotNet.Running;
using Inquiry.Benchmarks;

if (args.Contains("--capture-plans", StringComparer.OrdinalIgnoreCase))
{
    await QueryPlanCapture.RunAsync();
    return;
}

// Default: run every benchmark. Pass `--filter *Customer*` etc. to scope a run.
// Examples:
//   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks
//   dotnet run -c Release --project benchmarks/Inquiry.Benchmarks -- --filter *SelectAll*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal partial class Program { }
