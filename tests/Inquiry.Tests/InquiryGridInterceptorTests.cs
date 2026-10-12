using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Diagnostics;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Diagnostics;

namespace Inquiry.Tests;

/// <summary>
/// Multi-result (grid) commands raise the command interceptor callbacks on the ad-hoc and generated
/// overloads, with and without an ambient transaction (#244).
/// </summary>
[Collection("Inquiry telemetry")]
public sealed class InquiryGridInterceptorTests
{
    public static TheoryData<bool, bool> Routes => new()
    {
        { false, false },
        { false, true },
        { true, false },
        { true, true },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridRaisesExecutedWhenDisposed(bool generated, bool transacted)
    {
        var recorder = new EventInterceptor();

        await RunAsync(transacted, [recorder], async inquiry =>
        {
            await using (var grid = await QueryMultipleAsync(inquiry, "SELECT 1; SELECT 2", generated))
            {
                Assert.Equal(1L, await grid.ReadScalarAsync<long>());
                Assert.Equal(2L, await grid.ReadScalarAsync<long>());
                Assert.Equal(["initialized", "executing"], recorder.Events);
            }
        });

        Assert.Equal(["initialized", "executing", "executed"], recorder.Events);
        Assert.Single(recorder.Commands.Distinct());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridDisposedEarlyRaisesExecutedOnce(bool generated, bool transacted)
    {
        var recorder = new EventInterceptor();

        await RunAsync(transacted, [recorder], async inquiry =>
        {
            var grid = await QueryMultipleAsync(inquiry, "SELECT 1; SELECT 2", generated);
            Assert.Equal(1L, await grid.ReadScalarAsync<long>());
            await grid.DisposeAsync();
            await grid.DisposeAsync();
        });

        Assert.Equal(["initialized", "executing", "executed"], recorder.Events);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridExecutionFailureRaisesFailed(bool generated, bool transacted)
    {
        var recorder = new EventInterceptor();

        await RunAsync(transacted, [recorder], async inquiry =>
            await Assert.ThrowsAsync<SqliteException>(
                () => QueryMultipleAsync(inquiry, "SELECT * FROM NoSuchTable", generated)));

        Assert.Equal(["initialized", "executing", "failed"], recorder.Events);
        Assert.IsType<SqliteException>(recorder.Failure);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridReadFailureRaisesFailedOnceAndSkipsExecuted(bool generated, bool transacted)
    {
        var recorder = new EventInterceptor();

        await RunAsync(transacted, [recorder], async inquiry =>
        {
            await using var grid = await QueryMultipleAsync(inquiry, "SELECT 1 UNION ALL SELECT 2", generated);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => grid.ReadRowsAsync(_ => throw new InvalidOperationException("first")));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => grid.ReadRowsAsync(_ => throw new InvalidOperationException("second")));
        });

        Assert.Equal(["initialized", "executing", "failed"], recorder.Events);
        Assert.Equal("first", Assert.IsType<InvalidOperationException>(recorder.Failure).Message);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridReadCancellationRaisesFailed(bool generated, bool transacted)
    {
        var recorder = new EventInterceptor();
        using var cts = new CancellationTokenSource();

        await RunAsync(transacted, [recorder], async inquiry =>
        {
            await using var grid = await QueryMultipleAsync(inquiry, "SELECT 1; SELECT 2", generated);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grid.ReadScalarAsync<long>(cts.Token));
        });

        Assert.Equal(["initialized", "executing", "failed"], recorder.Events);
        Assert.IsAssignableFrom<OperationCanceledException>(recorder.Failure);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridExecutionCancellationRaisesFailedWithCallerToken(bool generated, bool transacted)
    {
        using var cts = new CancellationTokenSource();
        var recorder = new EventInterceptor { OnExecuting = cts.Cancel };

        await RunAsync(transacted, [recorder], async inquiry =>
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => QueryMultipleAsync(inquiry, "SELECT 1; SELECT 2", generated, cts.Token)));

        Assert.Equal(["initialized", "executing", "failed"], recorder.Events);
        var failure = Assert.IsAssignableFrom<OperationCanceledException>(recorder.Failure);
        Assert.Equal(cts.Token, failure.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task GridKeepsOneTelemetryLifecycleWhenTelemetryInterceptorIsRegistered(bool generated, bool transacted)
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InquiryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped) stopped.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        // Other tests may run grids at the same time. The root activity scopes the assertion to this test.
        using var root = new Activity("grid-interceptor-test").Start();
        var recorder = new EventInterceptor();
        var telemetry = new InquiryTelemetryInterceptor(new InquiryTelemetryOptions(), loggerFactory: null);

        await RunAsync(transacted, [telemetry, recorder], async inquiry =>
        {
            await using var grid = await QueryMultipleAsync(inquiry, "SELECT 1; SELECT 2", generated);
            await grid.ReadScalarAsync<long>();
            await grid.ReadScalarAsync<long>();
        });

        List<Activity> ours;
        lock (stopped) ours = stopped.Where(a => a.TraceId == root.TraceId).ToList();
        var span = Assert.Single(ours);
        Assert.Equal("QUERY_MULTIPLE", span.DisplayName);
        Assert.Equal("QUERY_MULTIPLE", recorder.ExecutingActivity);
        Assert.Equal(["initialized", "executing", "executed"], recorder.Events);
    }

    // Every caller passes a constant literal, so the INQ048 dynamic-text warning does not apply.
#pragma warning disable INQ048
    private static Task<InquiryGridReader> QueryMultipleAsync(
        IInquiry inquiry, string sql, bool generated, CancellationToken cancellationToken = default)
        => generated
            ? inquiry.QueryMultipleAsync(new InquiryGeneratedCommand<byte>(sql, default, static (_, _) => { }), cancellationToken)
            : inquiry.QueryMultipleAsync(new InquiryCommand(sql), cancellationToken);
#pragma warning restore INQ048

    private static async Task RunAsync(bool transacted, IInquiryCommandInterceptor[] interceptors, Func<IInquiry, Task> act)
    {
        var factory = new MemoryConnectionFactory();
        var pipeline = new InquiryRequestPipeline(factory, interceptors);
        using var services = new ServiceCollection().BuildServiceProvider();
        var inquiry = new DefaultInquiry(pipeline, factory, interceptors, services);
        if (!transacted)
        {
            await act(inquiry);
            return;
        }

        await using var transaction = await inquiry.BeginTransactionAsync();
        await act(inquiry);
        await transaction.CommitAsync();
    }

    private sealed class EventInterceptor : IInquiryCommandInterceptor
    {
        public List<string> Events { get; } = new();
        public List<DbCommand> Commands { get; } = new();
        public Exception? Failure { get; private set; }
        public string? ExecutingActivity { get; private set; }
        public Action? OnExecuting { get; init; }

        public ValueTask CommandInitializedAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
            => Record("initialized", context.Command);

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            ExecutingActivity = Activity.Current?.DisplayName;
            OnExecuting?.Invoke();
            return Record("executing", context.Command);
        }

        public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
            => Record("executed", context.Command);

        public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
        {
            Failure = context.Exception;
            return Record("failed", context.Command);
        }

        private ValueTask Record(string name, DbCommand command)
        {
            Events.Add(name);
            Commands.Add(command);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryConnectionFactory : IInquiryConnectionFactory
    {
        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
