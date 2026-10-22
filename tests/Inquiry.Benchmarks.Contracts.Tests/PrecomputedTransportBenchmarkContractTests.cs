using BenchmarkDotNet.Attributes;

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Raw_PrecomputedSqlFloor()
        {
            // The contract reads only the method name and attributes.
        }

        [Benchmark, PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Raw_PreSerializedJsonFloor()
        {
            // The contract reads only the method name and attributes.
        }
    }

    private sealed class MissingFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Raw_PrecomputedSql()
        {
            // The contract reads only the method name and attributes.
        }
    }

    private sealed class SelectedFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Selected_PrecomputedSqlFloor()
        {
            // The contract reads only the method name and attributes.
        }
    }

    private sealed class GeneratedFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Generated_PrecomputedSqlFloor()
        {
            // The contract reads only the method name and attributes.
        }
    }

    private sealed class ProductionFloorBenchmarks
    {
        [Benchmark, PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Production_PrecomputedSqlFloor()
        {
            // The contract reads only the method name and attributes.
        }
    }

    private sealed class MissingBenchmarkAttribute
    {
        [PrecomputedTransportBenchmark]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The contract reads public instance methods, so the fixture methods stay on the instance.")]
        public void Raw_PrecomputedSqlFloor()
        {
            // The contract reads only the method name and attributes.
        }
    }
}
