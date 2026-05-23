using System.Diagnostics;

namespace Inquiry;

public sealed class OpenTelemetryInquiryMiddleware : IInquiryMiddleware
{
    private readonly InquiryTelemetryOptions _options;

    public OpenTelemetryInquiryMiddleware()
        : this(new InquiryTelemetryOptions { Enabled = true })
    {
    }

    public OpenTelemetryInquiryMiddleware(InquiryOptions options)
        : this(options.Telemetry)
    {
    }

    public OpenTelemetryInquiryMiddleware(InquiryTelemetryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<InquiryResponse> InvokeAsync(
        InquiryRequestContext context,
        InquiryRequestDelegate next,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next(context).ConfigureAwait(false);
        }

        using var activity = InquiryDiagnostics.ActivitySource.StartActivity($"Inquiry {context.Operation}", ActivityKind.Client);
        activity?.SetTag("db.inquiry.operation", context.Operation.ToString());
        activity?.SetTag("db.inquiry.entity", context.EntityType?.FullName);
        activity?.SetTag("db.operation", context.Operation.ToString());
        if (_options.IncludeSqlText && context.CommandText is not null)
        {
            activity?.SetTag("db.statement", context.CommandText);
        }

        InquiryDiagnostics.ActiveOperations.Add(1);
        try
        {
            var response = await next(context).ConfigureAwait(false);
            activity?.SetTag("db.inquiry.rows_affected", response.RowsAffected);
            InquiryDiagnostics.OperationDuration.Record(response.Elapsed.TotalMilliseconds, InquiryDiagnostics.CreateTags(context));
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            InquiryDiagnostics.OperationFailures.Add(1, InquiryDiagnostics.CreateTags(context));
            throw;
        }
        finally
        {
            InquiryDiagnostics.ActiveOperations.Add(-1);
        }
    }
}
