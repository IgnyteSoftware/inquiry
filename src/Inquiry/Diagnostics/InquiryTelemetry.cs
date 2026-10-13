using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Inquiry.Diagnostics;

/// <summary>
/// Names of the <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> Inquiry emits when telemetry is enabled via
/// <c>AddInquiryTelemetry()</c>. Subscribe an OpenTelemetry <c>TracerProvider</c> /
/// <c>MeterProvider</c> (or any <see cref="ActivityListener"/> / <see cref="MeterListener"/>)
/// to these names:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(InquiryTelemetry.ActivitySourceName))
///     .WithMetrics(m => m.AddMeter(InquiryTelemetry.MeterName));
/// </code>
/// </summary>
public static class InquiryTelemetry
{
    /// <summary>The name of the <see cref="ActivitySource"/> Inquiry emits database spans on.</summary>
    public const string ActivitySourceName = "Inquiry";

    /// <summary>The name of the <see cref="Meter"/> Inquiry emits database metrics on.</summary>
    public const string MeterName = "Inquiry";

    private static readonly string Version =
        typeof(InquiryTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(InquiryTelemetry).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal static readonly Meter Meter = new(MeterName, Version);

    /// <summary>
    /// Duration of database commands executed by the Inquiry pipeline, following the OpenTelemetry
    /// database semantic conventions (<c>db.client.operation.duration</c>, seconds). Failed commands
    /// carry an <c>error.type</c> tag, so error rate is derivable from the same instrument.
    /// </summary>
    internal static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        "db.client.operation.duration",
        unit: "s",
        description: "Duration of database commands executed by the Inquiry pipeline.");

    internal static readonly Histogram<double> BulkConnectionOpenDuration = Meter.CreateHistogram<double>(
        "inquiry.bulk_insert.connection_open.duration",
        unit: "s",
        description: "Duration of opening or acquiring the connection used for a native bulk insert.");

    internal static readonly Histogram<double> BulkCopyDuration = Meter.CreateHistogram<double>(
        "inquiry.bulk_insert.copy.duration",
        unit: "s",
        description: "Duration of the provider-native copy phase of a bulk insert.");

    internal static string MapDbSystem(System.Data.Common.DbCommand command) => command.GetType().Name switch
    {
        "SqliteCommand" => "sqlite",
        "SqlCommand" => "microsoft.sql_server",
        "NpgsqlCommand" => "postgresql",
        "MySqlCommand" => "mysql",
        "OracleCommand" => "oracle.db",
        _ => "other_sql",
    };

    internal static string MapDbSystem(System.Data.Common.DbConnection connection) => connection.GetType().Name switch
    {
        "SqliteConnection" => "sqlite",
        "SqlConnection" => "microsoft.sql_server",
        "NpgsqlConnection" or "NpgsqlDataSource" => "postgresql",
        "MySqlConnection" => "mysql",
        "OracleConnection" => "oracle.db",
        _ => "other_sql",
    };

    internal static string MapDbSystem(Connections.IInquiryConnectionFactory factory) => factory.GetType().Name switch
    {
        "SqliteInquiryConnectionFactory" => "sqlite",
        "SqlServerInquiryConnectionFactory" => "microsoft.sql_server",
        "PostgreSqlInquiryConnectionFactory" => "postgresql",
        "MySqlInquiryConnectionFactory" => "mysql",
        "MariaDbInquiryConnectionFactory" => "mysql",
        "OracleInquiryConnectionFactory" => "oracle.db",
        _ => "other_sql",
    };

    /// <summary>
    /// Best-effort extraction of the first table name from common SQL patterns.
    /// </summary>
    internal static string? ExtractTableName(string? commandText)
    {
        if (commandText is null) return null;
        return InquiryTelemetryInterceptor.TableName(commandText);
    }

    /// <summary>
    /// Reads <see cref="InquiryTelemetryOptions.RecordCommandText"/> from the registered telemetry
    /// interceptor. Grid, batch, and bulk spans start outside the interceptor, so they use this to
    /// apply the same redaction. Without <c>AddInquiryTelemetry()</c> the option default applies.
    /// </summary>
    internal static bool RecordsCommandDetails(IEnumerable<Interceptors.IInquiryCommandInterceptor> interceptors)
    {
        foreach (var interceptor in interceptors)
        {
            if (interceptor is InquiryTelemetryInterceptor telemetry)
            {
                return telemetry.RecordCommandText;
            }
        }

        return new InquiryTelemetryOptions().RecordCommandText;
    }

    internal static string ErrorType(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    /// <summary>
    /// Returns the provider failure that cancellation normalization kept as the inner exception,
    /// or <see langword="null"/> when the failure is not a wrapped provider error.
    /// </summary>
    internal static Exception? CancellationCause(Exception exception)
        => exception is OperationCanceledException { InnerException: { } cause } && cause is not OperationCanceledException
            ? cause
            : null;

    /// <summary>Marks <paramref name="activity"/> failed, applying the command-detail redaction.</summary>
    internal static void SetError(Activity activity, Exception exception, bool recordDetails)
    {
        activity.SetTag("error.type", ErrorType(exception));
        var cause = CancellationCause(exception);
        if (cause is not null)
        {
            // The outer type stays cancellation so error rates keep their meaning. The cause type
            // carries no SQL, so it is recorded even when command details are redacted.
            activity.SetTag("inquiry.error.cause.type", ErrorType(cause));
        }

        // Provider messages can quote table, column, and constraint names, which is SQL shape.
        string? description = null;
        if (recordDetails)
        {
            description = cause is null ? exception.Message : exception.Message + " ---> " + cause.Message;
        }

        activity.SetStatus(ActivityStatusCode.Error, description);
    }
}
