using System.Data.Common;
using System.Diagnostics;

namespace Inquiry;

public sealed class InquiryOpenTelemetryOptions
{
    public bool IncludeCommandText { get; set; }

    public bool IncludeParameterValues { get; set; }

    public bool IncludeParameterNames { get; set; } = true;

    public bool RecordExceptionEvents { get; set; } = true;

    public Func<InquiryRequestContext, bool>? Filter { get; set; }

    public Action<Activity, InquiryRequestContext>? Enrich { get; set; }

    public Action<Activity, InquiryRequestContext, DbCommand>? EnrichWithDbCommand { get; set; }
}
