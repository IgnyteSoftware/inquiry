using Inquiry.Benchmarks.RepoDB;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

// SQLite returns Discontinued and whole-number UnitPrice values as Int64. These legs map them through
// RepoDB's and Dapper's own mappers, so they must read what the ADO.NET legs read (#556).
// The update legs share one static counter with CrudUpdateLegTests, so the two classes must not run in parallel.
[Collection(nameof(CrudUpdateLegTests))]
public sealed class SqliteIntegerMappingLegTests
{
    public static TheoryData<Type, string> IntegerMappedLegs() => new()
    {
        { typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks), "SelectAll_RepoDb" },
        { typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks), "SelectByField_RepoDb" },
        { typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks), "SelectByKey_RepoDb" },
        { typeof(global::Inquiry.Benchmarks.PaginationBenchmarks), "OffsetPage_RepoDb" },
        { typeof(global::Inquiry.Benchmarks.PredicateBenchmarks), "Search_RepoDb" },
        { typeof(global::Inquiry.Benchmarks.ProjectionAggregateBenchmarks), "Projection_Dapper" },
    };

    [Theory]
    [MemberData(nameof(IntegerMappedLegs))]
    public async Task LegReturnsWhatTheBaselineLegReturns(Type benchmarkType, string legName)
    {
        var benchmark = LinqToDbLegTests.CreateAtSmallestParams(benchmarkType);
        LinqToDbLegTests.Invoke(benchmark, "GlobalSetup");
        try
        {
            var leg = benchmarkType.GetMethod(legName)!;
            var baseline = LinqToDbLegTests.BaselineLeg(benchmarkType, LinqToDbLegTests.Category(leg));
            LinqToDbLegTests.Invoke(benchmark, "IterationSetup");
            var expected = await CrudUpdateLegTests.InvokeAsync(benchmark, baseline);
            LinqToDbLegTests.Invoke(benchmark, "IterationSetup");
            var actual = await CrudUpdateLegTests.InvokeAsync(benchmark, leg);

            Assert.NotNull(expected);
            Assert.NotNull(actual);
            if (expected is int)
            {
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            LinqToDbLegTests.Invoke(benchmark, "GlobalCleanup");
        }
    }

    // The seed writes only Discontinued = 0. Update_AdoNet alternates it between true and false, so the
    // key row is compared after two updates as well.
    [Fact]
    public async Task RepoDbKeyReadMatchesTheAdoNetKeyReadAfterEachUpdate()
    {
        var benchmarkType = typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks);
        var benchmark = LinqToDbLegTests.CreateAtSmallestParams(benchmarkType);
        LinqToDbLegTests.Invoke(benchmark, "GlobalSetup");
        try
        {
            var discontinued = new List<bool>();
            for (var round = 0; round < 3; round++)
            {
                if (round > 0)
                {
                    await CrudUpdateLegTests.InvokeAsync(benchmark, benchmarkType.GetMethod("Update_AdoNet")!);
                }

                var expected = await CrudUpdateLegTests.InvokeAsync(benchmark, benchmarkType.GetMethod("SelectByKey_AdoNet")!);
                var actual = (RdProduct)(await CrudUpdateLegTests.InvokeAsync(benchmark, benchmarkType.GetMethod("SelectByKey_RepoDb")!))!;

                Assert.NotNull(expected);
                foreach (var property in typeof(RdProduct).GetProperties())
                {
                    Assert.Equal(expected.GetType().GetProperty(property.Name)!.GetValue(expected), property.GetValue(actual));
                }

                discontinued.Add(actual.Discontinued);
            }

            Assert.Contains(true, discontinued);
            Assert.Contains(false, discontinued);
        }
        finally
        {
            LinqToDbLegTests.Invoke(benchmark, "GlobalCleanup");
        }
    }
}
