using System.Diagnostics;
using Inquiry.Tests.Fakes;

namespace Inquiry.Tests;

public sealed class OpenTelemetryTests
{
    [Fact]
    public async Task OpenTelemetryMiddleware_CreatesActivityWithDatabaseTagsAndEnrichment()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InquiryOpenTelemetry.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var connection = new RecordingDbConnection();
        var client = new InquiryClient(
            (_, _) => ValueTask.FromResult<System.Data.Common.DbConnection>(connection),
            InquirySqliteProvider.Instance,
            middleware: new[]
            {
                new OpenTelemetryInquiryMiddleware(new InquiryOpenTelemetryOptions
                {
                    IncludeCommandText = true,
                    IncludeParameterValues = true,
                    EnrichWithDbCommand = (activity, _, command) =>
                    {
                        activity.SetTag("db.inquiry.test_command_type", command.CommandType.ToString());
                    }
                })
            },
            ownsConnections: false);

        await client.ExecuteStoredProcedureAsync("dbo.TouchUser", new { id = 7 });

        var activity = Assert.Single(activities);
        Assert.Equal("Inquiry CALL", activity.DisplayName);
        Assert.Equal("sqlite", activity.GetTagItem("db.system"));
        Assert.Equal("sqlite", activity.GetTagItem("db.inquiry.provider"));
        Assert.Equal("StoredProcedureExecute", activity.GetTagItem("db.inquiry.operation"));
        Assert.Equal("StoredProcedure", activity.GetTagItem("db.inquiry.command_type"));
        Assert.Equal("dbo.TouchUser", activity.GetTagItem("db.statement"));
        Assert.Equal(7, activity.GetTagItem("db.query.parameter.id"));
        Assert.Equal("StoredProcedure", activity.GetTagItem("db.inquiry.test_command_type"));
    }

    [Fact]
    public async Task OpenTelemetryMiddleware_FilterCanSuppressInstrumentation()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == InquiryOpenTelemetry.InstrumentationName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var connection = new RecordingDbConnection();
        var client = new InquiryClient(
            (_, _) => ValueTask.FromResult<System.Data.Common.DbConnection>(connection),
            InquirySqliteProvider.Instance,
            middleware: new[]
            {
                new OpenTelemetryInquiryMiddleware(new InquiryOpenTelemetryOptions
                {
                    Filter = _ => false
                })
            },
            ownsConnections: false);

        await client.ExecuteAsync("UPDATE users SET email = @email", new { email = "filtered@example.com" });

        Assert.Empty(activities);
    }
}
