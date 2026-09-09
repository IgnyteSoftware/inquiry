using BenchmarkDotNet.Attributes;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Inquiry.Materialization;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inquiry.Benchmarks;

[MemoryDiagnoser]
public class ActiveTelemetryBenchmarks
{
    public enum ListenerMode { Inactive, Tracing, Metrics, Both }

    [ParamsAllValues]
    public ListenerMode Listeners { get; set; }

    private SqliteConnection _keeper = null!;
    private ServiceProvider _provider = null!;
    private IInquiry _inquiry = null!;
    private ActivityListener? _activities;
    private MeterListener? _metrics;
    private static readonly InquiryCommand Query = new("SELECT Id FROM TelemetryRows ORDER BY Id");
    private static readonly InquiryCommand Grid = new("SELECT Id FROM TelemetryRows ORDER BY Id; SELECT Id FROM TelemetryRows ORDER BY Id");
    private static readonly int[] Items = Enumerable.Range(1, 10).ToArray();

    [GlobalSetup]
    public async Task Setup()
    {
        var connectionString = $"Data Source=Telemetry-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connectionString);
        await _keeper.OpenAsync();
        using (var command = _keeper.CreateCommand())
        {
            command.CommandText = "CREATE TABLE TelemetryRows (Id INTEGER PRIMARY KEY, Value INTEGER NOT NULL);" +
                string.Join("", Items.Select(id => $"INSERT INTO TelemetryRows VALUES ({id}, 0);"));
            await command.ExecuteNonQueryAsync();
        }

        _provider = new ServiceCollection()
            .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.None)
            .AddInquirySqlite(connectionString)
            .AddInquiryTelemetry()
            .BuildServiceProvider();
        _inquiry = _provider.GetRequiredService<IInquiry>();

        var activityCount = 0;
        var measurementCount = 0;
        AttachListeners(_ => activityCount++, (_, _, _, _) => measurementCount++);
        foreach (var operation in new Func<Task<int>>[] { BufferedQuery, ConsumedStream, ConsumedGrid, Batch })
        {
            activityCount = measurementCount = 0;
            var result = await operation();
            var expected = operation == ConsumedGrid ? 110 : operation == Batch ? 10 : 55;
            var expectedObservations = operation == Batch ? Items.Length + 1 : 1;
            if (result != expected
                || activityCount != (Listeners is ListenerMode.Tracing or ListenerMode.Both ? expectedObservations : 0)
                || measurementCount != (Listeners is ListenerMode.Metrics or ListenerMode.Both ? expectedObservations : 0))
                throw new InvalidOperationException($"Invalid result or telemetry for {operation.Method.Name}: {result}, {activityCount} spans, {measurementCount} measurements.");
        }

        DetachListeners();
        // Counting belongs to setup. Timed consumers do no aggregation or export work.
        AttachListeners(static _ => { }, static (_, _, _, _) => { });
    }

    [Benchmark]
    public async Task<int> BufferedQuery()
    {
        var rows = await _inquiry.QueryListAsync<Row, RowMaterializer>(Query, default);
        var sum = 0;
        foreach (var row in rows) sum += row.Id;
        return sum;
    }

    [Benchmark]
    public async Task<int> ConsumedStream()
    {
        var sum = 0;
        await foreach (var row in _inquiry.QueryAsync<Row, RowMaterializer>(Query, default))
            sum += row.Id;
        return sum;
    }

    [Benchmark]
    public async Task<int> ConsumedGrid()
    {
        await using var grid = await _inquiry.QueryMultipleAsync(Grid);
        var first = await grid.ReadListAsync<Row, RowMaterializer>(default);
        var second = await grid.ReadListAsync<Row, RowMaterializer>(default);
        var sum = 0;
        foreach (var row in first) sum += row.Id;
        foreach (var row in second) sum += row.Id;
        return sum;
    }

    [Benchmark]
    public Task<int> Batch() => _inquiry.ExecuteBatchAsync(
        "UPDATE TelemetryRows SET Value = 1 - Value WHERE Id = @id", Items,
        static (target, id) =>
        {
            var parameter = target.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = id;
            target.AddParameter(parameter);
        });

    private void AttachListeners(Action<Activity> stopped, MeasurementCallback<double> measured)
    {
        if (Listeners is ListenerMode.Tracing or ListenerMode.Both)
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == InquiryTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped
            };
            ActivitySource.AddActivityListener(_activities);
        }
        if (Listeners is ListenerMode.Metrics or ListenerMode.Both)
        {
            _metrics = new MeterListener
            {
                InstrumentPublished = static (instrument, listener) =>
                {
                    if (instrument.Meter.Name == InquiryTelemetry.MeterName && instrument.Name == "db.client.operation.duration")
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _metrics.SetMeasurementEventCallback(measured);
            _metrics.Start();
        }
    }

    private void DetachListeners()
    {
        _activities?.Dispose();
        _metrics?.Dispose();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DetachListeners();
        _provider.Dispose();
        _keeper.Dispose();
    }

    private sealed class Row { public int Id { get; init; } }
    private readonly struct RowMaterializer : IInquiryEntityMaterializer<Row>
    {
        public bool IsInquirySequentialAccessSafe => true;
        public Row Materialize(DbDataReader reader) => new() { Id = reader.GetInt32(0) };
    }
}
