namespace Inquiry.Diagnostics;

/// <summary>
/// Configuration for the optional Inquiry telemetry layer, supplied via the
/// <c>AddInquiryTelemetry(Action&lt;InquiryTelemetryOptions&gt;)</c> overload.
/// </summary>
public sealed class InquiryTelemetryOptions
{
    /// <summary>
    /// Gets or sets whether SQL details are recorded on spans and log messages. Defaults to
    /// <see langword="true"/>: Inquiry SQL is built at compile time from constant templates and never
    /// embeds values — data flows only through bound parameters, and parameter values are never
    /// recorded. Set to <see langword="false"/> if even the SQL shape (table/column names) must not
    /// leave the process.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, command, grid, batch, bulk, and transaction spans omit
    /// <c>db.query.text</c> and <c>db.collection.name</c>, and the span status has no description.
    /// Error log messages carry the exception type instead of the exception, because provider
    /// messages can quote table, column, and constraint names. Exception type names
    /// (<c>error.type</c>, <c>inquiry.error.cause.type</c>), operation names, and durations are
    /// always recorded.
    /// </remarks>
    public bool RecordCommandText { get; set; } = true;
}
