using BenchmarkDotNet.Attributes;
using Inquiry.Benchmarks.Contracts;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class PrecomputedTransportBenchmarkContractTests
{
    [Fact]
    public void ValidateAcceptsExplicitPrecomputedAndPreSerializedFloors()
    {
        PrecomputedTransportBenchmarkContract.Validate(typeof(ValidBenchmarks));
    }

    [Theory]
    [InlineData(typeof(MissingFloorBenchmarks), "must contain Precomputed or PreSerialized and Floor")]
    [InlineData(typeof(SelectedFloorBenchmarks), "cannot contain Selected")]
    [InlineData(typeof(GeneratedFloorBenchmarks), "cannot contain Generated")]
    [InlineData(typeof(ProductionFloorBenchmarks), "cannot contain Production")]
    [InlineData(typeof(MissingBenchmarkAttribute), "must also be marked")]
    public void ValidateRejectsMisleadingPrecomputedTransportNames(Type type, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PrecomputedTransportBenchmarkContract.Validate(type));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private sealed class ValidBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        public void Raw_PrecomputedSqlFloor() { }

        [Benchmark, PrecomputedTransportBenchmark]
        public void Raw_PreSerializedJsonFloor() { }
    }

    private sealed class MissingFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        public void Raw_PrecomputedSql() { }
    }

    private sealed class SelectedFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        public void Selected_PrecomputedSqlFloor() { }
    }

    private sealed class GeneratedFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        public void Generated_PrecomputedSqlFloor() { }
    }

    private sealed class ProductionFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        public void Production_PrecomputedSqlFloor() { }
    }

    private sealed class MissingBenchmarkAttribute
    {
        [PrecomputedTransportBenchmark]
        public void Raw_PrecomputedSqlFloor() { }
    }
}
