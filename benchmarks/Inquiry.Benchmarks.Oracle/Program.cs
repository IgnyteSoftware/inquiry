using System.Runtime.CompilerServices;
using Inquiry.Benchmarks.Oracle;

if (args is [ProcessColdStart.ProbeSwitch, var leg])
{
    return await ProcessColdStart.ProbeAsync(leg);
}

if (args is [ProcessColdStart.DriverSwitch, .. var rest])
{
    return await ProcessColdStart.RunAsync(rest);
}

return RunBenchmarks(args);

// Kept out of the entry point, so that a cold-start probe does not load BenchmarkDotNet.
[MethodImpl(MethodImplOptions.NoInlining)]
static int RunBenchmarks(string[] args)
{
    BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    return 0;
}
