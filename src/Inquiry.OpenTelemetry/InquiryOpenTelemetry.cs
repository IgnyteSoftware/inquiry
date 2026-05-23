using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inquiry;

public static class InquiryOpenTelemetry
{
    public const string InstrumentationName = "Inquiry";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);

    public static readonly Meter Meter = new(InstrumentationName);

    public static readonly Counter<long> Operations = Meter.CreateCounter<long>(
        "inquiry.operations",
        description: "Cumulative number of Inquiry operations executed.");

    public static readonly Counter<long> OperationFailures = Meter.CreateCounter<long>(
        "inquiry.operation.failures",
        description: "Cumulative number of failed Inquiry operations.");

    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "inquiry.operation.duration",
        unit: "ms",
        description: "Duration of Inquiry operations.");

    public static readonly Histogram<long> RowsAffected = Meter.CreateHistogram<long>(
        "inquiry.operation.rows_affected",
        unit: "rows",
        description: "Rows affected or materialized by Inquiry operations.");

#if !NET6_0
    public static readonly UpDownCounter<long> ActiveOperations = Meter.CreateUpDownCounter<long>(
        "inquiry.operation.active",
        description: "Number of active Inquiry operations.");
#endif

    internal static void AddActiveOperation(long delta, KeyValuePair<string, object?>[] tags)
    {
#if !NET6_0
        ActiveOperations.Add(delta, tags);
#endif
    }

    internal static void AddException(Activity activity, Exception exception)
    {
        var tags = new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.StackTrace
        };

        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }

    internal static KeyValuePair<string, object?>[] CreateTags(InquiryRequestContext context)
    {
        return new[]
        {
            new KeyValuePair<string, object?>("db.system", NormalizeDbSystem(context.ProviderName)),
            new KeyValuePair<string, object?>("db.inquiry.provider", context.ProviderName),
            new KeyValuePair<string, object?>("db.inquiry.operation", context.Operation.ToString()),
            new KeyValuePair<string, object?>("db.inquiry.command_type", context.CommandType.ToString()),
            new KeyValuePair<string, object?>("db.inquiry.entity", context.EntityType?.FullName)
        };
    }

    internal static string? NormalizeDbSystem(string? providerName)
    {
        return providerName?.ToLowerInvariant() switch
        {
            "postgresql" => "postgresql",
            "sqlite" => "sqlite",
            "sqlserver" => "mssql",
            "mysql" => "mysql",
            "mariadb" => "mariadb",
            "oracle" => "oracle",
            _ => providerName
        };
    }

    internal static string DbOperationName(InquiryRequestContext context)
    {
        if (context.CommandType == CommandType.StoredProcedure)
        {
            return "CALL";
        }

        return context.Operation switch
        {
            InquiryOperation.Find or InquiryOperation.Select or InquiryOperation.RawQuery or InquiryOperation.StoredProcedureQuery => "SELECT",
            InquiryOperation.Insert or InquiryOperation.InsertMany => "INSERT",
            InquiryOperation.Update => "UPDATE",
            InquiryOperation.Delete => "DELETE",
            InquiryOperation.Upsert => "UPSERT",
            InquiryOperation.RawExecute or InquiryOperation.StoredProcedureExecute => "EXECUTE",
            _ => context.Operation.ToString().ToUpperInvariant()
        };
    }
}
