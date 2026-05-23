using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Inquiry;

public static class InquiryDiagnostics
{
    public const string InstrumentationName = "Inquiry";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);

    public static readonly Meter Meter = new(InstrumentationName);

    public static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "inquiry.operation.duration",
        unit: "ms",
        description: "Duration of Inquiry database operations.");

    public static readonly Counter<long> OperationFailures = Meter.CreateCounter<long>(
        "inquiry.operation.failures",
        description: "Number of failed Inquiry database operations.");

#if NET6_0
    public static readonly Counter<long> ActiveOperations = Meter.CreateCounter<long>(
        "inquiry.operation.active",
        description: "Number of active Inquiry database operations.");
#else
    public static readonly UpDownCounter<long> ActiveOperations = Meter.CreateUpDownCounter<long>(
        "inquiry.operation.active",
        description: "Number of active Inquiry database operations.");
#endif

    internal static KeyValuePair<string, object?>[] CreateTags(InquiryRequestContext context)
    {
        return new[]
        {
            new KeyValuePair<string, object?>("db.inquiry.operation", context.Operation.ToString()),
            new KeyValuePair<string, object?>("db.inquiry.entity", context.EntityType?.FullName),
            new KeyValuePair<string, object?>("db.inquiry.command_type", context.CommandType.ToString())
        };
    }
}
