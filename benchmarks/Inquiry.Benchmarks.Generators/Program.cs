using BenchmarkDotNet.Running;
using Inquiry.Benchmarks.Generators;

if (args is ["--build-evidence", var destination])
{
    await ConsumerBuildEvidence.RunAsync(destination, [25, 100, 500]);
}
else if (args is ["--build-evidence", var sizedDestination, var entityCounts])
{
    // For example "10,100,1000,5000".
    int[] counts = [.. entityCounts.Split(',').Select(static count => int.Parse(count, System.Globalization.CultureInfo.InvariantCulture))];

    // The fixture edits Model1 and Model2, and each count names its own sample directories.
    if (counts.Any(static count => count < 3) || counts.Distinct().Count() != counts.Length)
    {
        throw new ArgumentException("Entity counts must be distinct and at least 3.");
    }

    await ConsumerBuildEvidence.RunAsync(sizedDestination, counts);
}
else
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
