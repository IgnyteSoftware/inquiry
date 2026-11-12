using System.Globalization;
using Inquiry;
using Inquiry.Benchmarks.Aot;

[assembly: InquiryDialect("Sqlite")]

return args switch
{
    [AotBenchmark.ProbeSwitch, var leg] => await AotBenchmark.ProbeAsync(leg),
    ["--throughput", var leg, var operations] => await AotBenchmark.ThroughputAsync(leg, int.Parse(operations, CultureInfo.InvariantCulture)),
    ["--compare", .. var directories] when directories.Length > 0 => await AotBenchmark.CompareAsync(directories),
    _ => AotBenchmark.Usage(),
};
