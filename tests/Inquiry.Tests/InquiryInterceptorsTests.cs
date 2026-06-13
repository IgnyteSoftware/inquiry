using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// The Inquiry.Interceptors companion package: slow-query logging fires only at/over the
/// threshold, and the sqlcommenter tagger appends application/traceparent comments that the
/// database still executes.
/// </summary>
public sealed class InquiryInterceptorsTests
{
    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class ListLogger : ILogger
        {
            private readonly List<string> _messages;
            public ListLogger(List<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_messages)
                {
                    _messages.Add(logLevel + ": " + formatter(state, exception));
                }
            }
        }
    }

    /// <summary>Captures the command text seen at execution time. Registered last, so it observes
    /// any mutations earlier interceptors (the sqlcommenter) made.</summary>
    private sealed class CapturingInterceptor : IInquiryCommandInterceptor
    {
        public string LastCommandText { get; private set; } = string.Empty;

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            LastCommandText = context.Command.CommandText;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Services { get; init; }
        public required SqliteConnection Keeper { get; init; }
        public required ListLoggerProvider Log { get; init; }
        public required CapturingInterceptor Captured { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Keeper.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateHarnessAsync(Action<IServiceCollection> configure)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "Interceptors_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var cmd = keeper.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Item (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL); INSERT INTO Item (Name) VALUES ('a');";
            await cmd.ExecuteNonQueryAsync();
        }

        var loggerProvider = new ListLoggerProvider();
        var captured = new CapturingInterceptor();
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(loggerProvider))
            .AddInquiry();
        configure(services);
        // Registered after the package interceptors so it sees their command-text mutations.
        services.AddSingleton<IInquiryCommandInterceptor>(captured);

        var provider = Inquiry.Sqlite.DependencyInjection.SqliteInquiryServiceCollectionExtensions
            .AddInquirySqlite(services, connectionString)
            .BuildServiceProvider();

        return new Harness { Services = provider, Keeper = keeper, Log = loggerProvider, Captured = captured };
    }

    [Fact]
    public async Task SlowQueryLoggingWarnsAtOrOverThreshold()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquirySlowQueryLogging(TimeSpan.FromTicks(1)));

        var inquiry = harness.Services.GetRequiredService<IInquiry>();
        var count = await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item");
        Assert.Equal(1L, count);

        var warning = Assert.Single(harness.Log.Messages, m => m.StartsWith("Warning", StringComparison.Ordinal));
        Assert.Contains("SELECT COUNT(*) FROM Item", warning);
        Assert.Contains("threshold", warning);
    }

    [Fact]
    public async Task SlowQueryLoggingStaysSilentUnderThreshold()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquirySlowQueryLogging(TimeSpan.FromMinutes(5)));

        var inquiry = harness.Services.GetRequiredService<IInquiry>();
        await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item");

        Assert.DoesNotContain(harness.Log.Messages, m => m.StartsWith("Warning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SqlCommenterAppendsApplicationAndTraceparentAndSqlStillExecutes()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquirySqlCommenter("checkout-api"));

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "InterceptorsTest",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("InterceptorsTest");
        using var activity = source.StartActivity("op");
        Assert.NotNull(activity);

        var inquiry = harness.Services.GetRequiredService<IInquiry>();
        var count = await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item");

        // The tag is execution-side only — results are unaffected, and the comment carried both keys.
        Assert.Equal(1L, count);
        Assert.Contains("application='checkout-api'", harness.Captured.LastCommandText);
        Assert.Contains("traceparent='00-" + activity!.TraceId + "-", harness.Captured.LastCommandText);
    }

    [Fact]
    public async Task SqlCommenterWithoutActivityOrApplicationLeavesTextUntouched()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquirySqlCommenter());

        var inquiry = harness.Services.GetRequiredService<IInquiry>();
        var count = await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item");

        Assert.Equal(1L, count);
        Assert.DoesNotContain("/*", harness.Captured.LastCommandText);
    }

    [Fact]
    public async Task NPlusOneDetectionWarnsWhenSameSqlRepeatsInScope()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquiryNPlusOneDetection(threshold: 2));
        var inquiry = harness.Services.GetRequiredService<IInquiry>();

        using (Inquiry.Interceptors.InquiryNPlusOneScope.BeginScope())
        {
            // Same parameterized SQL, different parameter each time — the N+1 signature.
            for (var id = 1; id <= 3; id++)
            {
                await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item WHERE Id = {id}");
            }
        }

        // Exactly one warning (fired when the count first reached the threshold), naming the SQL.
        var warning = Assert.Single(harness.Log.Messages, m => m.StartsWith("Warning", StringComparison.Ordinal));
        Assert.Contains("Possible N+1", warning);
        Assert.Contains("SELECT COUNT(*) FROM Item WHERE Id =", warning);
    }

    [Fact]
    public async Task NPlusOneDetectionStaysSilentOutsideAScope()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquiryNPlusOneDetection(threshold: 2));
        var inquiry = harness.Services.GetRequiredService<IInquiry>();

        // No scope active → no detection, even though the same SQL repeats.
        for (var id = 1; id <= 3; id++)
        {
            await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item WHERE Id = {id}");
        }

        Assert.DoesNotContain(harness.Log.Messages, m => m.Contains("Possible N+1"));
    }

    [Fact]
    public async Task NPlusOneDetectionStaysSilentBelowThreshold()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquiryNPlusOneDetection(threshold: 5));
        var inquiry = harness.Services.GetRequiredService<IInquiry>();

        using (Inquiry.Interceptors.InquiryNPlusOneScope.BeginScope())
        {
            for (var id = 1; id <= 3; id++)
            {
                await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item WHERE Id = {id}");
            }
        }

        // 3 executions < threshold 5 → no warning.
        Assert.DoesNotContain(harness.Log.Messages, m => m.Contains("Possible N+1"));
    }

    [Fact]
    public async Task NPlusOneDetectionFingerprintsThroughSqlCommenterTag()
    {
        // SqlCommenter (registered first) appends a per-trace comment that varies each execution; the
        // detector must strip it so the repeats still fingerprint together regardless of interceptor order.
        await using var harness = await CreateHarnessAsync(s =>
        {
            s.AddInquirySqlCommenter("checkout-api");
            s.AddInquiryNPlusOneDetection(threshold: 2);
        });

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "NPlusOneTest",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("NPlusOneTest");
        using var activity = source.StartActivity("op");
        Assert.NotNull(activity);

        var inquiry = harness.Services.GetRequiredService<IInquiry>();
        using (Inquiry.Interceptors.InquiryNPlusOneScope.BeginScope())
        {
            for (var id = 1; id <= 3; id++)
            {
                await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item WHERE Id = {id}");
            }
        }

        Assert.Single(harness.Log.Messages, m => m.Contains("Possible N+1"));
    }

    [Fact]
    public async Task NPlusOneDetectionDistinctSqlDoesNotAccumulate()
    {
        await using var harness = await CreateHarnessAsync(s => s.AddInquiryNPlusOneDetection(threshold: 2));
        var inquiry = harness.Services.GetRequiredService<IInquiry>();

        using (Inquiry.Interceptors.InquiryNPlusOneScope.BeginScope())
        {
            // Two different statements, each run once — not an N+1.
            await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item");
            await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Item WHERE Id = {1}");
        }

        Assert.DoesNotContain(harness.Log.Messages, m => m.Contains("Possible N+1"));
    }
}
