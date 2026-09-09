using BenchmarkDotNet.Running;
using Inquiry.Benchmarks.Generators;

if (args is ["--build-evidence", var destination])
    await ConsumerBuildEvidence.RunAsync(destination);
else
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
