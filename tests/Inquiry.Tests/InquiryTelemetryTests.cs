using Inquiry.BulkCopy;
using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Inquiry.Testing;
using Inquiry.Sqlite.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inquiry.Tests;

[CollectionDefinition("Inquiry telemetry", DisableParallelization = true)]
public sealed class InquiryTelemetryCollection
{
}

[Collection("Inquiry telemetry")]
public sealed class InquiryTelemetryTests
{
    [Fact]
    public void TelemetryActivationTracksObserversWithoutCaching()
    {
        var interceptor = new InquiryTelemetryInterceptor(new InquiryTelemetryOptions(), loggerFactory: null);
        Assert.False(interceptor.IsActive);

        var activities = new List<Activity>();
        using (CreateActivityListener(activities))
        {
            Assert.True(interceptor.IsActive);
        }

        Assert.False(interceptor.IsActive);
        Assert.True(new InquiryTelemetryInterceptor(
            new InquiryTelemetryOptions(),
            new RecordingLoggerFactory()).IsActive);
    }

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
        Assert.Equal("Items", activity.GetTagItem("db.collection.name"));
        Assert.Equal(1, activity.GetTagItem("db.response.affected_rows"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
        Assert.True(activity.Duration > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("INSERT INTO Items (Id) VALUES (1)", "Items")]
    [InlineData("insert into items (Id) values (1)", "items")]
    [InlineData("UPDATE Items SET Name = 'A'", "Items")]
    [InlineData("DELETE FROM Items WHERE Id = 1", "Items")]
    [InlineData("SELECT * FROM Items WHERE Id = 1", "Items")]
    [InlineData("SELECT Id FROM dbo.Items WHERE Id = 1", "Items")]
    [InlineData("INSERT INTO [Items] (Id) VALUES (1)", "Items")]
    [InlineData("INSERT INTO \"Items\" (Id) VALUES (1)", "Items")]
    [InlineData("INSERT INTO `Items` (Id) VALUES (1)", "Items")]
    [InlineData("SELECT 1", null)]
    [InlineData("EXEC sp_something", null)]
    // Qualified and escaped names report the table, not the schema or an inner fragment (#252).
    [InlineData("INSERT INTO [dbo].[Items] (Id) VALUES (1)", "Items")]
    [InlineData("INSERT INTO \"public\".\"Items\" (Id) VALUES (1)", "Items")]
    [InlineData("INSERT INTO `shop`.`Items` (Id) VALUES (1)", "Items")]
    [InlineData("SELECT * FROM [Sales].[dbo].[Items]", "Items")]
    [InlineData("SELECT * FROM Sales.dbo.Items", "Items")]
    [InlineData("SELECT * FROM dbo . Items", "Items")]
    [InlineData("SELECT * FROM [Odd]]Name]", "Odd]Name")]
    [InlineData("SELECT * FROM \"Odd\"\"Name\"", "Odd\"Name")]
    [InlineData("SELECT * FROM [Items", null)]
    // Whitespace and comments around keywords.
    [InlineData("SELECT *\nFROM Items", "Items")]
    [InlineData("SELECT *\tFROM\tItems", "Items")]
    [InlineData("SELECT Id\r\nFROM\r\n  Items", "Items")]
    [InlineData("-- leading comment\nSELECT * FROM /* inline */ Items", "Items")]
    // Only the outer statement's FROM counts; ambiguous sources get no tag.
    [InlineData("SELECT (SELECT COUNT(*) FROM Orders) AS N FROM Items", "Items")]
    [InlineData("SELECT a AS \" FROM Orders \", 'x FROM y' AS b FROM Items", "Items")]
    [InlineData("SELECT * FROM (SELECT * FROM Items) AS x", null)]
    [InlineData("SELECT * FROM dbo.GetItems(@id)", null)]
    [InlineData("SELECT 1; SELECT * FROM Items", null)]
    // Complete keywords and supported statement variants.
    [InlineData("INSERT INTOItems VALUES (1)", null)]
    [InlineData("DELETE FROMItems", null)]
    [InlineData("SELECT FromDate FROM Items", "Items")]
    [InlineData("INSERT OR REPLACE INTO Items (Id) VALUES (1)", "Items")]
    [InlineData("INSERT IGNORE INTO Items (Id) VALUES (1)", "Items")]
    [InlineData("UPDATE TOP (10) Items SET Name = 'A'", "Items")]
    [InlineData("UPDATE TOP (10) PERCENT [dbo].[Items] SET Name = 'A'", "Items")]
    [InlineData("UPDATE OR IGNORE Items SET Name = 'A'", "Items")]
    [InlineData("UPDATE Orders SET Name = 'A'", "Orders")]
    public void TableNameExtractsTableFromCommonSqlPatterns(string sql, string? expected)
    {
        Assert.Equal(expected, InquiryTelemetryInterceptor.TableName(sql));
    }

    private const string Sentinel = "SecretSentinel_7f3a";

    [Theory]
    [InlineData("command", false)]
    [InlineData("grid", false)]
    [InlineData("batch", false)]
    [InlineData("command", true)]
    [InlineData("grid", true)]
    [InlineData("batch", true)]
    public async Task RedactedTelemetryOmitsSqlDetailsOnEveryPipelineRoute(string route, bool transacted)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        var loggerFactory = new RecordingLoggerFactory();
        var interceptors = new IInquiryCommandInterceptor[]
        {
            new InquiryTelemetryInterceptor(new InquiryTelemetryOptions { RecordCommandText = false }, loggerFactory),
        };

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => RunFailingRouteAsync(route, transacted, interceptors));

        // The provider message names the table; this is what must not leave the process.
        Assert.Contains(Sentinel, exception.Message);
        Assert.Contains(activities, a => a.Status == ActivityStatusCode.Error
            && (string?)a.GetTagItem("error.type") == typeof(SqliteException).FullName);
        AssertNoSentinel(activities);
        Assert.All(activities, a => Assert.Null(a.GetTagItem("db.collection.name")));
        Assert.DoesNotContain(loggerFactory.Entries, e => e.Message.Contains(Sentinel) || e.Exception is not null);
        if (route == "command")
        {
            var failed = Assert.Single(loggerFactory.Entries, e => e.Level == LogLevel.Error);
            Assert.EndsWith("with " + typeof(SqliteException).FullName, failed.Message);
        }
    }

    [Theory]
    [InlineData("command")]
    [InlineData("grid")]
    [InlineData("batch")]
    public async Task DefaultTelemetryKeepsProviderErrorDetails(string route)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        var interceptors = new IInquiryCommandInterceptor[]
        {
            new InquiryTelemetryInterceptor(new InquiryTelemetryOptions(), loggerFactory: null),
        };

        await Assert.ThrowsAsync<SqliteException>(() => RunFailingRouteAsync(route, transacted: false, interceptors));

        Assert.Contains(activities, a => a.StatusDescription?.Contains(Sentinel) == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkInsertTelemetryFollowsRegisteredRedaction(bool recordCommandText)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        await using var fixture = await SqliteInquiryFixture.CreateAsync(services => services
            .AddInquiryTelemetry(o => o.RecordCommandText = recordCommandText)
            .AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier(throws: true)));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(
            null, Sentinel, new[] { "Id" }, static (item, _) => item.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) }));

        var bulkActivity = Assert.Single(activities, a => a.DisplayName == "BULK_INSERT");
        Assert.Equal(typeof(InvalidOperationException).FullName, bulkActivity.GetTagItem("error.type"));
        if (recordCommandText)
        {
            Assert.Equal(Sentinel, bulkActivity.GetTagItem("db.collection.name"));
            Assert.Contains(Sentinel, bulkActivity.StatusDescription);
        }
        else
        {
            AssertNoSentinel(activities);
            Assert.Null(bulkActivity.GetTagItem("db.collection.name"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationKeepsProviderErrorTypeWithoutReplayingTheWrite(bool recordCommandText)
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        var loggerFactory = new RecordingLoggerFactory();
        using var cancellation = new CancellationTokenSource();
        var factory = new NativeErrorOnCancelConnectionFactory(cancellation);
        var pipeline = new InquiryRequestPipeline(
            factory,
            new IInquiryCommandInterceptor[]
            {
                new InquiryTelemetryInterceptor(new InquiryTelemetryOptions { RecordCommandText = recordCommandText }, loggerFactory),
            });

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.ExecuteAsync(
            new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.IsType<NativeProviderException>(exception.InnerException);
        Assert.Equal(1, factory.ExecuteCount);

        var activity = Assert.Single(activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(OperationCanceledException).FullName, activity.GetTagItem("error.type"));
        Assert.Equal(typeof(NativeProviderException).FullName, activity.GetTagItem("inquiry.error.cause.type"));
        var failed = Assert.Single(loggerFactory.Entries, e => e.Level == LogLevel.Error);
        if (recordCommandText)
        {
            Assert.Contains(Sentinel, activity.StatusDescription);
            Assert.Same(exception, failed.Exception);
        }
        else
        {
            AssertNoSentinel(activities);
            Assert.Null(failed.Exception);
            Assert.EndsWith(
                $"with {typeof(OperationCanceledException).FullName} caused by {typeof(NativeProviderException).FullName}",
                failed.Message);
        }
    }

    private static async Task RunFailingRouteAsync(string route, bool transacted, IInquiryCommandInterceptor[] interceptors)
    {
        var factory = new TelemetryTestConnectionFactory("Data Source=:memory:");
        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        IInquiryRequestPipeline pipeline = transacted
            ? new TransactedInquiryRequestPipeline(connection, transaction, interceptors, factory, options: null)
            : new InquiryRequestPipeline(factory, interceptors);
        const string sql = $"INSERT INTO {Sentinel} (Id) VALUES (@id)";

        switch (route)
        {
            case "command":
                await pipeline.ExecuteAsync(new InquiryCommand(sql, new[] { new InquiryParameter("id", 1) }));
                break;
            case "grid":
                const string select = $"SELECT * FROM {Sentinel}";
                await using (await pipeline.QueryMultipleAsync(new InquiryCommand(select)))
                {
                }

                break;
            case "batch":
                await pipeline.ExecuteBatchAsync(sql, new[] { 1 }, static (target, item) =>
                {
                    var p = target.CreateParameter();
                    p.ParameterName = "@id";
                    p.Value = item;
                    target.AddParameter(p);
                });
                break;
        }
    }

    private static void AssertNoSentinel(IEnumerable<Activity> activities)
    {
        foreach (var activity in activities)
        {
            Assert.DoesNotContain(Sentinel, activity.DisplayName);
            Assert.DoesNotContain(Sentinel, activity.StatusDescription ?? "");
            foreach (var tag in activity.TagObjects)
            {
                Assert.DoesNotContain(Sentinel, tag.Value?.ToString() ?? "");
            }

            foreach (var activityEvent in activity.Events)
            {
                foreach (var tag in activityEvent.Tags)
                {
                    Assert.DoesNotContain(Sentinel, tag.Value?.ToString() ?? "");
                }
            }
        }
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task TelemetryCompletesActivityWhenListenerDetachesMidFlight(bool generated, bool transacted, bool fails)
    {
        Activity? started = null;
        ActivityListener? listener = null;
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InquiryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                started = activity;
                listener!.Dispose();
            },
        };
        using var listenerScope = listener;
        ActivitySource.AddActivityListener(listener);

        var execution = ExecuteCommandAsync(
                transacted,
                new IInquiryCommandInterceptor[] { new InquiryTelemetryInterceptor(new InquiryTelemetryOptions(), loggerFactory: null) },
                generated,
                fails);
        if (fails)
        {
            await Assert.ThrowsAsync<SqliteException>(() => execution);
        }
        else
        {
            await execution;
        }

        var activity = Assert.IsType<Activity>(started);
        Assert.True(activity.Duration > TimeSpan.Zero);
        Assert.Equal(fails ? ActivityStatusCode.Error : ActivityStatusCode.Unset, activity.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TelemetryIgnoresUnmatchedCompletionWhenListenerAttachesMidFlight(bool transacted)
    {
        var telemetry = new InquiryTelemetryInterceptor(new InquiryTelemetryOptions(), loggerFactory: null);
        using var attachingInterceptor = new ListenerAttachingInterceptor();

        await ExecuteCommandAsync(
            transacted,
            new IInquiryCommandInterceptor[] { telemetry, attachingInterceptor },
            generated: true);

        Assert.Empty(attachingInterceptor.StartedActivities);
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

    private static async Task ExecuteCommandAsync(
        bool transacted,
        IInquiryCommandInterceptor[] interceptors,
        bool generated,
        bool fails = false)
    {
        var factory = new TelemetryTestConnectionFactory("Data Source=:memory:");
        if (!transacted)
        {
            var pipeline = new InquiryRequestPipeline(factory, interceptors);
            await ExecuteAsync(pipeline, generated, fails);
            return;
        }

        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var transactedPipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            interceptors,
            factory,
            options: null);
        await ExecuteAsync(transactedPipeline, generated, fails);
    }

    private static Task<int> ExecuteAsync(IInquiryRequestPipeline pipeline, bool generated, bool fails)
    {
        if (generated)
        {
            return pipeline.ExecuteAsync(new InquiryGeneratedCommand<byte>(
                fails ? "SELECT * FROM NoSuchTable" : "SELECT 1",
                default,
                static (_, _) => { }));
        }

        return fails
            ? pipeline.ExecuteAsync(new InquiryCommand("SELECT * FROM NoSuchTable"))
            : pipeline.ExecuteAsync(new InquiryCommand("SELECT 1"));
    }

    private static async Task<(IInquiryRequestPipeline Pipeline, SqliteConnection Keeper)> CreatePipelineAsync(
        InquiryTelemetryOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        bool noInterceptors = false)
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

        var interceptors = noInterceptors
            ? Array.Empty<IInquiryCommandInterceptor>()
            : new IInquiryCommandInterceptor[] { new InquiryTelemetryInterceptor(options ?? new InquiryTelemetryOptions(), loggerFactory) };
        var pipeline = new InquiryRequestPipeline(
            new TelemetryTestConnectionFactory(connectionString),
            interceptors);
        return (pipeline, keeper);
    }

    [Fact]
    public async Task GridReaderEmitsSpanCoveringFullLifetime()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _k = keeper;

        await using (var grid = await pipeline.QueryMultipleAsync(
            new InquiryCommand("SELECT * FROM Items; SELECT COUNT(*) FROM Items")))
        {
            await grid.ReadListAsync<SimpleItem, SimpleItemMaterializer>(default);
            await grid.ReadScalarAsync<long>();
        }

        var gridActivity = Assert.Single(activities, a => a.DisplayName == "QUERY_MULTIPLE");
        Assert.Equal(ActivityKind.Client, gridActivity.Kind);
        Assert.Equal("sqlite", gridActivity.GetTagItem("db.system.name"));
        Assert.Equal("QUERY_MULTIPLE", gridActivity.GetTagItem("db.operation.name"));
        Assert.NotEqual(ActivityStatusCode.Error, gridActivity.Status);
        Assert.True(gridActivity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GridReaderRecordsDurationMetric()
    {
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _k = keeper;

        await using (var grid = await pipeline.QueryMultipleAsync(new InquiryCommand("SELECT 1")))
        {
            await grid.ReadScalarAsync<long>();
        }

        meterListener.Dispose();

        var gridMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "QUERY_MULTIPLE");
        Assert.True(gridMeasurement.Value >= 0);
        Assert.Equal("sqlite", gridMeasurement.Tags["db.system.name"]);
    }

    [Fact]
    public async Task GridReaderExecutionFailureRecordsErrorMetricAndSpan()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _k = keeper;

        await Assert.ThrowsAsync<SqliteException>(
            () => pipeline.QueryMultipleAsync(new InquiryCommand("SELECT * FROM NoSuchTable")));

        meterListener.Dispose();

        var gridActivity = Assert.Single(activities, a => a.DisplayName == "QUERY_MULTIPLE");
        Assert.Equal(ActivityStatusCode.Error, gridActivity.Status);
        Assert.Equal(typeof(SqliteException).FullName, gridActivity.GetTagItem("error.type"));

        var gridMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "QUERY_MULTIPLE");
        Assert.Equal(typeof(SqliteException).FullName, gridMeasurement.Tags["error.type"]);
    }

    [Fact]
    public async Task GridReaderReadFailureRecordsErrorMetricOnce()
    {
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(new InquiryTelemetryOptions());
        await using var _k = keeper;

        await pipeline.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'A', 1), (2, 'B', 0)"));

        await using (var grid = await pipeline.QueryMultipleAsync(
            new InquiryCommand("SELECT * FROM Items")))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => grid.ReadSingleOrDefaultAsync<SimpleItem, SimpleItemMaterializer>(default));
        }

        meterListener.Dispose();

        var batchMeasurements = measurements.Where(m => (string?)m.Tags["db.operation.name"] == "QUERY_MULTIPLE").ToList();
        var errorMeasurement = Assert.Single(batchMeasurements, m => m.Tags.ContainsKey("error.type"));
        Assert.Equal(typeof(InvalidOperationException).FullName, errorMeasurement.Tags["error.type"]);
    }

    [Fact]
    public async Task BatchExecutionEmitsSpanWithRowCount()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var (pipeline, keeper) = await CreatePipelineAsync(noInterceptors: true);
        await using var _k = keeper;

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@id, @name, @active)",
            new[] { (1, "Alpha", 1), (2, "Beta", 0) },
            static (target, item) => BindBatchItem(target, item));

        Assert.Equal(2, total);
        var batchActivity = Assert.Single(activities, a => a.DisplayName == "EXECUTE_BATCH");
        Assert.Equal(ActivityKind.Client, batchActivity.Kind);
        Assert.Equal("sqlite", batchActivity.GetTagItem("db.system.name"));
        Assert.Equal("EXECUTE_BATCH", batchActivity.GetTagItem("db.operation.name"));
        Assert.Equal("Items", batchActivity.GetTagItem("db.collection.name"));
        Assert.Equal(2, batchActivity.GetTagItem("db.response.affected_rows"));
        Assert.NotEqual(ActivityStatusCode.Error, batchActivity.Status);
        Assert.True(batchActivity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task BatchExecutionRecordsDurationMetric()
    {
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(noInterceptors: true);
        await using var _k = keeper;

        await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@id, @name, @active)",
            new[] { (1, "Alpha", 1) },
            static (target, item) => BindBatchItem(target, item));

        meterListener.Dispose();

        var batchMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "EXECUTE_BATCH");
        Assert.True(batchMeasurement.Value >= 0);
        Assert.Equal("sqlite", batchMeasurement.Tags["db.system.name"]);
    }

    [Fact]
    public async Task BatchExecutionFailureRecordsErrorMetricAndSpan()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        var (pipeline, keeper) = await CreatePipelineAsync(noInterceptors: true);
        await using var _k = keeper;

        await Assert.ThrowsAsync<SqliteException>(() =>
            pipeline.ExecuteBatchAsync(
                "INSERT INTO NoSuchTable (Id) VALUES (@id)",
                new[] { 1 },
                static (target, item) =>
                {
                    var p = target.CreateParameter();
                    p.ParameterName = "@id";
                    p.Value = item;
                    target.AddParameter(p);
                }));

        meterListener.Dispose();

        var batchActivity = Assert.Single(activities, a => a.DisplayName == "EXECUTE_BATCH");
        Assert.Equal(ActivityStatusCode.Error, batchActivity.Status);
        Assert.Equal(typeof(SqliteException).FullName, batchActivity.GetTagItem("error.type"));

        var batchMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "EXECUTE_BATCH");
        Assert.Equal(typeof(SqliteException).FullName, batchMeasurement.Tags["error.type"]);
    }

    [Fact]
    public async Task BatchExecutionNoListenerDoesNotAllocateActivity()
    {
        var (pipeline, keeper) = await CreatePipelineAsync(noInterceptors: true);
        await using var _k = keeper;

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@id, @name, @active)",
            new[] { (1, "Alpha", 1) },
            static (target, item) => BindBatchItem(target, item));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task BulkInsertEmitsSpanWithTableNameAndRowCount()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
            services.AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier(rowCount: 5)));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(
            null, "Items", new[] { "Id", "Name", "IsActive" },
            static (item, ordinal) => ordinal switch { 0 => item.Id, 1 => item.Name, _ => item.IsActive });

        var count = await inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) });

        Assert.Equal(5, count);
        var bulkActivity = Assert.Single(activities, a => a.DisplayName == "BULK_INSERT");
        Assert.Equal(ActivityKind.Client, bulkActivity.Kind);
        Assert.Equal("sqlite", bulkActivity.GetTagItem("db.system.name"));
        Assert.Equal("BULK_INSERT", bulkActivity.GetTagItem("db.operation.name"));
        Assert.Equal("Items", bulkActivity.GetTagItem("db.collection.name"));
        Assert.Equal(5L, bulkActivity.GetTagItem("db.response.affected_rows"));
        Assert.Equal("dedicated", bulkActivity.GetTagItem("inquiry.bulk_insert.connection_mode"));
        Assert.Contains(bulkActivity.Events, static activityEvent => activityEvent.Name == "connection.opened");
        Assert.Contains(bulkActivity.Events, static activityEvent => activityEvent.Name == "copy.completed");
        Assert.NotEqual(ActivityStatusCode.Error, bulkActivity.Status);
        Assert.True(bulkActivity.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task BulkInsertRecordsDurationMetric()
    {
        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name is
                "db.client.operation.duration" or
                "inquiry.bulk_insert.connection_open.duration" or
                "inquiry.bulk_insert.copy.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
            services.AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier(rowCount: 3)));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(
            null, "Items", new[] { "Id" },
            static (item, _) => item.Id);

        await inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) });

        meterListener.Dispose();

        var bulkMeasurement = Assert.Single(measurements, m =>
            m.Tags.TryGetValue("db.operation.name", out var operation) && (string?)operation == "BULK_INSERT");
        Assert.True(bulkMeasurement.Value >= 0);
        Assert.Equal("sqlite", bulkMeasurement.Tags["db.system.name"]);
        Assert.Single(measurements, m => m.Tags.ContainsKey("inquiry.bulk_insert.connection_mode") && m.Value == 0.001);
        Assert.Single(measurements, m => m.Tags.ContainsKey("inquiry.bulk_insert.connection_mode") && m.Value == 0.002);
    }

    [Fact]
    public async Task BulkInsertFailureRecordsErrorMetricAndSpan()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);

        var measurements = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags) tagMap[tag.Key] = tag.Value;
            lock (measurements) measurements.Add((value, tagMap));
        });
        meterListener.Start();

        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
            services.AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier(throws: true)));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(
            null, "Items", new[] { "Id" },
            static (item, _) => item.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) }));

        meterListener.Dispose();

        var bulkActivity = Assert.Single(activities, a => a.DisplayName == "BULK_INSERT");
        Assert.Equal(ActivityStatusCode.Error, bulkActivity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, bulkActivity.GetTagItem("error.type"));

        var bulkMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "BULK_INSERT");
        Assert.Equal(typeof(InvalidOperationException).FullName, bulkMeasurement.Tags["error.type"]);
    }

    [Fact]
    public async Task BulkInsertCancellationRecordsOperationCanceledErrorType()
    {
        var activities = new List<Activity>();
        using var listener = CreateActivityListener(activities);
        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
            services.AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier()));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(null, "Items", new[] { "Id" }, static (item, _) => item.Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) }, cancellationToken: cancellation.Token));

        var bulkActivity = Assert.Single(activities, activity => activity.DisplayName == "BULK_INSERT");
        Assert.Equal(typeof(OperationCanceledException).FullName, bulkActivity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, bulkActivity.Status);
    }

    private static void BindBatchItem(InquiryParameterTarget target, (int Id, string Name, int IsActive) item)
    {
        var id = target.CreateParameter();
        id.ParameterName = "@id";
        id.Value = item.Id;
        target.AddParameter(id);

        var name = target.CreateParameter();
        name.ParameterName = "@name";
        name.Value = item.Name;
        target.AddParameter(name);

        var active = target.CreateParameter();
        active.ParameterName = "@active";
        active.Value = item.IsActive;
        target.AddParameter(active);
    }

    private sealed record SimpleItem(int Id, string Name, bool IsActive);

    private struct SimpleItemMaterializer : IInquiryEntityMaterializer<SimpleItem>
    {
        public SimpleItem Materialize(DbDataReader reader)
            => new(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2));
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

    private sealed class ListenerAttachingInterceptor : IInquiryCommandInterceptor, IDisposable
    {
        private ActivityListener? _listener;

        public List<Activity> StartedActivities { get; } = new();

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == InquiryTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = StartedActivities.Add,
            };
            ActivitySource.AddActivityListener(_listener);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => _listener?.Dispose();
    }

    private sealed class NativeProviderException : DbException
    {
        public NativeProviderException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Models a driver that reports its own error when the caller cancels mid-flight, as SqlClient
    /// does for an attention-cancelled command. The message names a table, like real driver errors.
    /// </summary>
    private sealed class NativeErrorOnCancelConnectionFactory : IInquiryConnectionFactory
    {
        private readonly CancellationTokenSource _cancellation;
        private int _executeCount;

        public NativeErrorOnCancelConnectionFactory(CancellationTokenSource cancellation) => _cancellation = cancellation;

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            return new NativeErrorConnection(connection, this);
        }

        internal Task<int> ExecuteAsync()
        {
            Interlocked.Increment(ref _executeCount);
            _cancellation.Cancel();
            return FailAsync();

            // The task stays pending while the pipeline registers its cancellation callback, so the
            // pipeline sees cancellation win the race, as it would for a slow driver.
            static async Task<int> FailAsync()
            {
                await Task.Delay(100);
                throw new NativeProviderException($"Operation cancelled by user while writing {Sentinel}.");
            }
        }
    }

    private sealed class NativeErrorConnection : DbConnection
    {
        private readonly SqliteConnection _inner;
        private readonly NativeErrorOnCancelConnectionFactory _factory;

        public NativeErrorConnection(SqliteConnection inner, NativeErrorOnCancelConnectionFactory factory)
        {
            _inner = inner;
            _factory = factory;
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource!;
        public override string ServerVersion => _inner.ServerVersion;
        public override System.Data.ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => _inner.BeginTransaction(isolationLevel);
        protected override DbCommand CreateDbCommand() => new NativeErrorCommand(_inner.CreateCommand(), _factory);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class NativeErrorCommand : DbCommand
    {
        private readonly SqliteCommand _inner;
        private readonly NativeErrorOnCancelConnectionFactory _factory;

        public NativeErrorCommand(SqliteCommand inner, NativeErrorOnCancelConnectionFactory factory)
        {
            _inner = inner;
            _factory = factory;
        }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get => _inner.CommandText; set => _inner.CommandText = value; }
        public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
        public override System.Data.CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
        public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }
        public override System.Data.UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => _inner.Connection; set => _inner.Connection = (SqliteConnection?)value; }
        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;
        protected override DbTransaction? DbTransaction { get => _inner.Transaction; set => _inner.Transaction = (SqliteTransaction?)value; }

        public override void Cancel() => _inner.Cancel();
        public override void Prepare() => _inner.Prepare();
        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => _inner.ExecuteReader(behavior);
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => _inner.ExecuteScalar();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => _factory.ExecuteAsync();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class FakeBulkCopier : IInquiryBulkCopier
    {
        private readonly long _rowCount;
        private readonly bool _throws;

        public FakeBulkCopier(long rowCount = 0, bool throws = false)
        {
            _rowCount = rowCount;
            _throws = throws;
        }

        public Task<long> BulkInsertAsync<TEntity>(
            InquiryBulkInsertDefinition<TEntity> definition,
            IEnumerable<TEntity> rows,
            InquiryBulkInsertContext context,
            CancellationToken cancellationToken = default)
            where TEntity : class
        {
            context.RecordConnectionOpened(TimeSpan.FromMilliseconds(1));
            cancellationToken.ThrowIfCancellationRequested();
            if (_throws)
            {
                context.RecordCopyCompleted(TimeSpan.FromMilliseconds(2));
                throw new InvalidOperationException($"Bulk insert into {definition.Table} failed.");
            }

            context.RecordCopyCompleted(TimeSpan.FromMilliseconds(2), _rowCount);
            return Task.FromResult(_rowCount);
        }
    }
}
