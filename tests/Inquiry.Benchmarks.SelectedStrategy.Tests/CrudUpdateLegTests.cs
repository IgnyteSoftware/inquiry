using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

[Collection(nameof(CrudUpdateLegTests))]
public sealed class CrudUpdateLegTests
{
    public static TheoryData<Type> SqliteCrudClasses() => new()
    {
        typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.CustomerCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ShipperCrudBenchmarks),
    };

    [Theory]
    [MemberData(nameof(SqliteCrudClasses))]
    public async Task EveryUpdateLegUpdatesOneRowOnEveryInvocation(Type benchmarkType)
    {
        var benchmark = Activator.CreateInstance(benchmarkType)!;
        benchmarkType.GetProperty("Rows")!.SetValue(benchmark, 1000);
        Invoke(benchmark, "GlobalSetup");
        try
        {
            var legs = UpdateLegs(benchmarkType);
            Assert.NotEmpty(legs);

            // Call each leg twice in a row, over several rounds, so each leg follows both itself and the other legs.
            for (var round = 0; round < 3; round++)
            {
                foreach (var leg in legs)
                {
                    for (var call = 0; call < 2; call++)
                    {
                        var result = await InvokeAsync(benchmark, leg);
                        Assert.True(
                            result is 1 or true,
                            $"{benchmarkType.Name}.{leg.Name} round {round} call {call} returned {result ?? "null"}, expected one updated row.");
                    }
                }
            }
        }
        finally
        {
            Invoke(benchmark, "GlobalCleanup");
        }
    }

    internal static MethodInfo[] UpdateLegs(Type benchmarkType) => benchmarkType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(static method => method.GetCustomAttribute<BenchmarkAttribute>() is not null
            && method.GetCustomAttribute<BenchmarkCategoryAttribute>()?.Categories
                .Any(static category => category is "Update" or "CancellationUpdate") == true)
        .OrderBy(static method => method.Name, StringComparer.Ordinal)
        .ToArray();

    internal static async Task<object?> InvokeAsync(object benchmark, MethodInfo leg)
    {
        var task = (Task)leg.Invoke(benchmark, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static void Invoke(object benchmark, string methodName)
        => benchmark.GetType().GetMethod(methodName)!.Invoke(benchmark, null);
}
