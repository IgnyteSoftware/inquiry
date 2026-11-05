using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Inquiry.Benchmarks.SqlServer.Tests;

public sealed class ShipperUpdateLegTests
{
    [Fact]
    public async Task EveryUpdateLegChangesTheTargetRowOnEveryInvocation()
    {
        var legs = typeof(ShipperBenchmarks)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.GetCustomAttribute<BenchmarkAttribute>() is not null
                && method.GetCustomAttribute<BenchmarkCategoryAttribute>()?.Categories.Contains("Update") == true)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Contains(legs, static leg => leg.Name == nameof(ShipperBenchmarks.Update_Dlg));

        // The container is shared by the process, so the second instance starts from a row the first one updated.
        for (var instance = 0; instance < 2; instance++)
        {
            var benchmark = new ShipperBenchmarks { Rows = 1000 };
            benchmark.GlobalSetup();
            try
            {
                for (var round = 0; round < 3; round++)
                {
                    // Each leg runs twice in a row, so it follows both itself and the other legs.
                    foreach (var leg in legs.SelectMany(static leg => new[] { leg, leg }))
                    {
                        var before = await ReadTargetAsync(benchmark);
                        var task = (Task)leg.Invoke(benchmark, null)!;
                        await task;
                        var result = task.GetType().GetProperty("Result")!.GetValue(task);
                        var after = await ReadTargetAsync(benchmark);

                        // DLG returns only "the procedure ran", so the row read-back is what proves its update.
                        Assert.True(result is 1 or true, $"{leg.Name} returned {result ?? "null"}, expected one updated row.");
                        Assert.NotEqual(before, after);
                    }
                }
            }
            finally
            {
                benchmark.GlobalCleanup();
            }
        }
    }

    private static async Task<(string CompanyName, string? Phone)> ReadTargetAsync(ShipperBenchmarks benchmark)
    {
        var shipper = await benchmark.SelectByKey_AdoNet();
        Assert.NotNull(shipper);
        return (shipper.CompanyName, shipper.Phone);
    }
}
