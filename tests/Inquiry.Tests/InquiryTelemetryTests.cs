using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Inquiry.Interceptors;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inquiry.Tests;

public sealed class InquiryTelemetryTests
{
    [Fact]
    public async Task TelemetryInterceptorEmitsSpanWithDatabaseSemanticTags()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _ = keeper;

        var affected = await pipeline.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
                new InquiryParameter("IsActive", 1),
            }));

        Assert.Equal(1, affected);
        var activity = Assert.Single(activities);
        Assert.Equal("INSERT", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("sqlite", activity.GetTagItem("db.system.name"));
        Assert.Equal("INSERT", activity.GetTagItem("db.operation.name"));
        Assert.StartsWith("INSERT INTO Items", (string?)activity.GetTagItem("db.query.text"));
        Assert.Equal(1, activity.GetTagItem("db.response.affected_rows"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
        Assert.True(activity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task TelemetryInterceptorOmitsQueryTextWhenDisabled()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions { RecordCommandText = false });
        await using var _ = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));

        var activity = Assert.Single(activities);
        Assert.Null(activity.GetTagItem("db.query.text"));
    }

    [Fact]
    public async Task TelemetryInterceptorMarksFailedCommands()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _ = keeper;

        await Assert.ThrowsAsync<SqliteException>(
            () => pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO NoSuchTable (Id) VALUES (1)")));

        var activity = Assert.Single(activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(SqliteException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task TelemetryInterceptorRecordsDurationMetric()
    {
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                tagMap[tag.Key] = tag.Value;
            }

            lock (measurements)
            {
                measurements.Add((value, tagMap));
            }
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _ = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));
        await Assert.ThrowsAsync<SqliteException>(
            () => pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO NoSuchTable (Id) VALUES (1)")));

        meterListener.Dispose();

        Assert.Equal(2, measurements.Count);
        var success = measurements[0];
        Assert.True(success.Value >= 0);
        Assert.Equal("sqlite", success.Tags["db.system.name"]);
        Assert.Equal("INSERT", success.Tags["db.operation.name"]);
        Assert.False(success.Tags.ContainsKey("error.type"));

        var failure = measurements[1];
        Assert.Equal(typeof(SqliteException).FullName, failure.Tags["error.type"]);
    }

    [Fact]
    public async Task TelemetryInterceptorLogsExecutedAndFailedCommands()
    {
        var loggerFactory = new RecordingLoggerFactory();
        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions(), loggerFactory);
        await using var _ = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));
        await Assert.ThrowsAsync<SqliteException>(
            () => pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO NoSuchTable (Id) VALUES (1)")));

        Assert.Contains(loggerFactory.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Executing INSERT"));
        Assert.Contains(loggerFactory.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("Executed INSERT"));
        var failed = Assert.Single(loggerFactory.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("Failed INSERT", failed.Message);
        Assert.IsType<SqliteException>(failed.Exception);
    }

    [Fact]
    public void AddInquiryTelemetryRegistersInterceptor()
    {
        var services = new ServiceCollection();
        services.AddInquiryTelemetry();

        using var provider = services.BuildServiceProvider();
        var interceptor = Assert.Single(provider.GetServices<IInquiryCommandInterceptor>());
        Assert.IsType<InquiryTelemetryInterceptor>(interceptor);
    }

    private static ActivityListener CreateActivityListener(List<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InquiryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (stopped)
                {
                    stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task<(IInquiryRequestPipeline Pipeline, SqliteConnection Keeper)> CreatePipelineAsync(
        InquiryTelemetryOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryTelemetry_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var command = keeper.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, IsActive INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var pipeline = new InquiryRequestPipeline(
            new TelemetryTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { new InquiryTelemetryInterceptor(options, loggerFactory) });
        return (pipeline, keeper);
    }

    private sealed class TelemetryTestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public TelemetryTestConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerFactory _owner;

            public RecordingLogger(RecordingLoggerFactory owner) => _owner = owner;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_owner.Entries)
                {
                    _owner.Entries.Add((logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
