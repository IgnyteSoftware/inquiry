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
    public void TableNameExtractsTableFromCommonSqlPatterns(string sql, string? expected)
    {
        Assert.Equal(expected, InquiryTelemetryInterceptor.TableName(sql));
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
            services.AddSingleton<IInquiryBulkCopier>(new FakeBulkCopier(rowCount: 3)));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<SimpleItem>(
            null, "Items", new[] { "Id" },
            static (item, _) => item.Id);

        await inquiry.BulkInsertAsync(definition, new[] { new SimpleItem(1, "A", true) });

        meterListener.Dispose();

        var bulkMeasurement = Assert.Single(measurements, m => (string?)m.Tags["db.operation.name"] == "BULK_INSERT");
        Assert.True(bulkMeasurement.Value >= 0);
        Assert.Equal("sqlite", bulkMeasurement.Tags["db.system.name"]);
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
            CancellationToken cancellationToken = default)
            where TEntity : class
        {
            if (_throws) throw new InvalidOperationException("Bulk insert failed.");
            return Task.FromResult(_rowCount);
        }
    }
}
