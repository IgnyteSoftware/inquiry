namespace Inquiry.Diagnostics;

/// <summary>
/// Configuration for the optional Inquiry telemetry layer, supplied via the
/// <c>AddInquiryTelemetry(Action&lt;InquiryTelemetryOptions&gt;)</c> overload.
/// </summary>
public sealed class InquiryTelemetryOptions
{
    /// <summary>
    /// Gets or sets whether the SQL command text is recorded on spans (<c>db.query.text</c>) and
    /// debug log messages. Defaults to <see langword="true"/>: Inquiry SQL is built at compile time
    /// from constant templates and never embeds values — data flows only through bound parameters,
    /// and parameter values are never recorded. Set to <see langword="false"/> if even the SQL
    /// shape (table/column names) must not leave the process.
    /// </summary>
    public bool RecordCommandText { get; set; } = true;
}
