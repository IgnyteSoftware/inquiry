using System.Collections;
using Inquiry.Benchmarks.Contracts;
using Inquiry.Benchmarks.SqlServer;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer.Tests;

public sealed class CollectionTransportBenchmarkIntegrationTests
{
    [Fact]
    public async Task StandardFixtureTransportsPreserveExactCollectionSemantics()
    {
        await using var database = await SqlServerCollectionBenchmarkDatabase.CreateAsync();
        await SqlServerCollectionCorrectness.VerifyAsync(database);
        var evidence = await SqlServerCollectionEvidenceCollector.CollectAsync(database);
        Assert.Empty(SqlServerCollectionEvidenceValidator.Validate(evidence));
    }

    [Fact]
    public async Task MissingGeneratedTvpArtifactFailsBeforeCollection()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SqlServerCollectionBenchmarkDatabase.CreateAsync(applyProviderArtifacts: false));
        Assert.Contains("TVP artifact before collection", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public async Task DirectTvpExecutionReleasesBinderSourceWhenExecutionFails()
    {
        await using var command = new SqlCommand { CommandText = SqlServerCollectionBenchmarks.TvpSql };
        var source = new TrackingEnumerable([1, 2]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlServerCollectionBenchmarks.ExecuteTvpCommandAsync(
                command, "[dbo].[Inquiry_Tvp_test]", source));

        Assert.Equal(1, source.DisposeCount);
    }

    private sealed class TrackingEnumerable(IEnumerable<int> values) : IEnumerable<int>
    {
        public int DisposeCount { get; private set; }

        public IEnumerator<int> GetEnumerator() => new Enumerator(values.GetEnumerator(), this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(IEnumerator<int> inner, TrackingEnumerable owner) : IEnumerator<int>
        {
            public int Current => inner.Current;
            object IEnumerator.Current => Current;
            public bool MoveNext() => inner.MoveNext();
            public void Reset() => inner.Reset();
            public void Dispose()
            {
                inner.Dispose();
                owner.DisposeCount++;
            }
        }
    }
}
