using Inquiry.Commands;
using Inquiry.Entities;
using Inquiry.Interceptors;
using Inquiry.SqlServer.Parameters;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Data.Common;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("TvpLifetimeItem")]
public sealed class TvpLifetimeItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn(Length = 32)] public string Name { get; set; } = string.Empty;
}

public partial class TvpLifetimeStore : InquiryStore<TvpLifetimeItem>
{
    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere("Id", Compare.In)]
    public partial IAsyncEnumerable<TvpLifetimeItem> StreamAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class TvpResourceLifetimeIntegrationTests
{
    private const string Ddl = "CREATE TABLE [TvpLifetimeItem] ([Id] INT NOT NULL PRIMARY KEY, [Name] NVARCHAR(32) NOT NULL); INSERT [TvpLifetimeItem] VALUES (1, N'one'), (2, N'two');";
    private readonly SqlServerContainerFixture _fixture;

    public TvpResourceLifetimeIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InterceptorFailureDisposesBoundSourceExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvpinterceptor", services =>
            services.AddSingleton<IInquiryCommandInterceptor>(new ThrowingExecutingInterceptor()));
        var source = new ProbeReadOnlyList<int>([1, 2]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.GetRequiredService<TvpLifetimeStore>().DeleteAllAsync(source));

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [SkippableFact]
    public async Task PreExecuteCancellationDisposesBoundSourceExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        using var cts = new CancellationTokenSource();
        await using var harness = await CreateAsync("tvpcancel", services =>
            services.AddSingleton<IInquiryCommandInterceptor>(new CancelingExecutingInterceptor(cts)));
        var source = new ProbeReadOnlyList<int>([1, 2]);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.GetRequiredService<TvpLifetimeStore>().DeleteAllAsync(source, cts.Token));

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [SkippableFact]
    public async Task ClosedConnectionFailureDisposesBoundSourceExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvpclosed", services =>
            services.AddSingleton<IInquiryCommandInterceptor>(new ClosingExecutingInterceptor()));
        var source = new ProbeReadOnlyList<int>([1, 2]);

        await Assert.ThrowsAnyAsync<Exception>(() => harness.GetRequiredService<TvpLifetimeStore>().DeleteAllAsync(source));

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [SkippableFact]
    public async Task StreamingEarlyExitDisposesBoundSourceExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvpstream", null);
        var source = new ProbeReadOnlyList<int>([1, 2]);

        await foreach (var item in harness.GetRequiredService<TvpLifetimeStore>().StreamAsync(source))
        {
            Assert.Equal(1, item.Id);
            break;
        }

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [SkippableFact]
    public async Task GridEarlyDisposeReleasesBoundSourceExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvpgrid", null);
        var source = new ProbeReadOnlyList<int>([1, 2]);
        var command = new InquiryCommand(
            "SELECT [Id], [Name] FROM [TvpLifetimeItem] ORDER BY [Id]; SELECT COUNT(*) FROM [TvpLifetimeItem];",
            dbCommand => InquiryTvpParameter.Bind(
                dbCommand,
                "@unused",
                source,
                "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]",
                InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));

        await using (var grid = await harness.GetRequiredService<global::Inquiry.IInquiry>().QueryMultipleAsync(command))
        {
            // Deliberately leave both result sets unread.
        }

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, source.GetEnumeratorCount);
    }

    [SkippableFact]
    public async Task LargeSuccessfulExecutionStreamsOnceAndDisposesExactlyOnce()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvplarge", null);
        var source = new ProbeReadOnlyList<int>(Enumerable.Range(1, 25_000).ToArray());

        Assert.Equal(2, await harness.GetRequiredService<TvpLifetimeStore>().DeleteAllAsync(source));

        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [SkippableFact]
    public async Task AmbientTransactionRollbackReleasesSourceAndRestoresRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvprollback", null);
        var inquiry = harness.GetRequiredService<global::Inquiry.IInquiry>();
        var store = harness.GetRequiredService<TvpLifetimeStore>();
        var source = new ProbeReadOnlyList<int>([1, 2]);

        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            Assert.Equal(2, await store.DeleteAllAsync(source));
            await transaction.RollbackAsync();
        }

        Assert.Equal(2, await store.CountAsync());
        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [SkippableFact]
    public async Task InFlightCancellationReleasesSourceWithoutExecutingDml()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync("tvpinflightcancel", null);
        var inquiry = harness.GetRequiredService<global::Inquiry.IInquiry>();
        var source = new ProbeReadOnlyList<int>([1, 2]);
        var command = new InquiryCommand(
            "WAITFOR DELAY '00:00:10'; SELECT COUNT(*) FROM @ids;",
            dbCommand => InquiryTvpParameter.Bind(
                dbCommand,
                "@ids",
                source,
                "[dbo].[Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c]",
                InquiryTvpDescriptor.Get("int", 0, 10, 0, false)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() => inquiry.ExecuteScalarAsync<int>(command, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(exception is OperationCanceledException ||
            exception is SqlException sqlException && sqlException.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, await harness.GetRequiredService<TvpLifetimeStore>().CountAsync());
        Assert.Equal(1, source.GetEnumeratorCount);
        Assert.Equal(1, source.DisposeCount);
    }

    private Task<SqlServerTestHarness> CreateAsync(string prefix, Action<IServiceCollection>? configure)
        => SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            Ddl,
            prefix,
            configureServices: configure);

    private sealed class ThrowingExecutingInterceptor : IInquiryCommandInterceptor
    {
        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("interceptor failure after TVP binding");
    }

    private sealed class CancelingExecutingInterceptor : IInquiryCommandInterceptor
    {
        private readonly CancellationTokenSource _source;
        public CancelingExecutingInterceptor(CancellationTokenSource source) => _source = source;

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ClosingExecutingInterceptor : IInquiryCommandInterceptor
    {
        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            context.Command.Connection!.Close();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly IReadOnlyList<T> _items;
        public ProbeReadOnlyList(IReadOnlyList<T> items) => _items = items;

        public int Count => _items.Count;
        public T this[int index] => _items[index];
        public int GetEnumeratorCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly ProbeReadOnlyList<T> _owner;
            private int _index = -1;
            private bool _disposed;

            public Enumerator(ProbeReadOnlyList<T> owner) => _owner = owner;
            public T Current => _owner._items[_index];
            object? IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _owner._items.Count;
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.DisposeCount++;
            }
        }
    }
}
