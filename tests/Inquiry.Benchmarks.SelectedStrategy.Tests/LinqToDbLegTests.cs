using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

public sealed class LinqToDbLegTests
{
    private static readonly string[] _writeCategories = ["Insert", "Update", "Delete", "Upsert"];

    public static TheoryData<Type> SqliteClassesWithLinqToDbReads() => new()
    {
        typeof(global::Inquiry.Benchmarks.ColdStartBenchmarks),
        typeof(global::Inquiry.Benchmarks.ConcurrencyBenchmarks),
        typeof(global::Inquiry.Benchmarks.CustomerCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ShipperCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ProjectionAggregateBenchmarks),
        typeof(global::Inquiry.Benchmarks.PaginationBenchmarks),
        typeof(global::Inquiry.Benchmarks.PredicateBenchmarks),
        typeof(global::Inquiry.Benchmarks.InCardinalityBenchmarks),
        typeof(global::Inquiry.Benchmarks.StreamingBenchmarks),
    };

    public static TheoryData<Type> SqliteCrudClasses() => new()
    {
        typeof(global::Inquiry.Benchmarks.CustomerCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks),
        typeof(global::Inquiry.Benchmarks.ShipperCrudBenchmarks),
    };

    [Theory]
    [MemberData(nameof(SqliteClassesWithLinqToDbReads))]
    public async Task EveryLinqToDbReadReturnsWhatTheBaselineLegReturns(Type benchmarkType)
    {
        var benchmark = CreateAtSmallestParams(benchmarkType);
        Invoke(benchmark, "GlobalSetup");
        try
        {
            var reads = Legs(benchmarkType)
                .Where(static leg => leg.Name.EndsWith("_LinqToDb", StringComparison.Ordinal)
                    && !_writeCategories.Contains(Category(leg)))
                .ToArray();
            Assert.NotEmpty(reads);

            foreach (var read in reads)
            {
                var baseline = BaselineLeg(benchmarkType, Category(read));
                Invoke(benchmark, "IterationSetup");
                var expected = await CrudUpdateLegTests.InvokeAsync(benchmark, baseline);
                Invoke(benchmark, "IterationSetup");
                var actual = await CrudUpdateLegTests.InvokeAsync(benchmark, read);

                Assert.NotNull(actual);
                if (expected is int or long or decimal)
                {
                    Assert.Equal(Convert.ToDecimal(expected, CultureInfo.InvariantCulture), Convert.ToDecimal(actual, CultureInfo.InvariantCulture));
                }
                else
                {
                    Assert.NotNull(expected);
                }
            }
        }
        finally
        {
            Invoke(benchmark, "GlobalCleanup");
        }
    }

    [Theory]
    [MemberData(nameof(SqliteCrudClasses))]
    public async Task LinqToDbUpsertUpdatesTheSeededRowOnEveryInvocation(Type benchmarkType)
    {
        var benchmark = CreateAtSmallestParams(benchmarkType);
        Invoke(benchmark, "GlobalSetup");
        try
        {
            var upsert = benchmarkType.GetMethod("Upsert_LinqToDb")!;
            var countAll = benchmarkType.GetMethod("SelectAll_AdoNet")!;
            var rowsBefore = await CrudUpdateLegTests.InvokeAsync(benchmark, countAll);

            for (var call = 0; call < 3; call++)
            {
                Assert.Equal(1, await CrudUpdateLegTests.InvokeAsync(benchmark, upsert));
            }

            Assert.Equal(rowsBefore, await CrudUpdateLegTests.InvokeAsync(benchmark, countAll));
        }
        finally
        {
            Invoke(benchmark, "GlobalCleanup");
        }
    }

    internal static object CreateAtSmallestParams(Type benchmarkType)
    {
        var benchmark = Activator.CreateInstance(benchmarkType)!;
        foreach (var property in benchmarkType.GetProperties())
        {
            if (property.GetCustomAttribute<ParamsAttribute>() is { } values)
            {
                property.SetValue(benchmark, values.Values.Min());
            }
        }

        return benchmark;
    }

    internal static MethodInfo[] Legs(Type benchmarkType) => benchmarkType
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(static method => method.GetCustomAttribute<BenchmarkAttribute>() is not null)
        .OrderBy(static method => method.Name, StringComparer.Ordinal)
        .ToArray();

    internal static string Category(MethodInfo leg)
        => leg.GetCustomAttribute<BenchmarkCategoryAttribute>()!.Categories.Single();

    // Some categories have a Dapper baseline instead of an ADO.NET one.
    internal static MethodInfo BaselineLeg(Type benchmarkType, string category) => Legs(benchmarkType)
        .Where(leg => Category(leg) == category)
        .Single(static leg => leg.GetCustomAttribute<BenchmarkAttribute>()!.Baseline);

    internal static void Invoke(object benchmark, string methodName)
        => benchmark.GetType().GetMethod(methodName)?.Invoke(benchmark, null);
}
