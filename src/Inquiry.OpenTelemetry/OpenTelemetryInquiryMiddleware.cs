using System.Diagnostics;

namespace Inquiry;

public sealed class OpenTelemetryInquiryMiddleware : IInquiryMiddleware
{
    private readonly InquiryOpenTelemetryOptions _options;

    public OpenTelemetryInquiryMiddleware()
        : this(new InquiryOpenTelemetryOptions())
    {
    }

    public OpenTelemetryInquiryMiddleware(InquiryOpenTelemetryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<InquiryResponse> InvokeAsync(
        InquiryRequestContext context,
        InquiryRequestDelegate next,
        CancellationToken cancellationToken)
    {
        if (_options.Filter is not null && !_options.Filter(context))
        {
            return await next(context).ConfigureAwait(false);
        }

        var tags = InquiryOpenTelemetry.CreateTags(context);
        InquiryOpenTelemetry.AddActiveOperation(1, tags);
        using var activity = InquiryOpenTelemetry.ActivitySource.StartActivity(
            CreateActivityName(context),
            ActivityKind.Client);

        EnrichActivity(activity, context);

        try
        {
            var response = await next(context).ConfigureAwait(false);
            activity?.SetTag("db.inquiry.rows_affected", response.RowsAffected);
            InquiryOpenTelemetry.Operations.Add(1, tags);
            InquiryOpenTelemetry.OperationDuration.Record(response.Elapsed.TotalMilliseconds, tags);
            if (response.RowsAffected is not null)
            {
                InquiryOpenTelemetry.RowsAffected.Record(response.RowsAffected.Value, tags);
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (_options.RecordExceptionEvents && activity is not null)
            {
                InquiryOpenTelemetry.AddException(activity, ex);
            }

            InquiryOpenTelemetry.OperationFailures.Add(1, tags);
            throw;
        }
        finally
        {
            InquiryOpenTelemetry.AddActiveOperation(-1, tags);
        }
    }

    private static string CreateActivityName(InquiryRequestContext context)
    {
        var dbOperation = InquiryOpenTelemetry.DbOperationName(context);
        return $"Inquiry {dbOperation}";
    }

    private void EnrichActivity(Activity? activity, InquiryRequestContext context)
    {
        if (activity is null)
        {
            return;
        }

        var dbOperation = InquiryOpenTelemetry.DbOperationName(context);
        activity.SetTag("db.system", InquiryOpenTelemetry.NormalizeDbSystem(context.ProviderName));
        activity.SetTag("db.operation", dbOperation);
        activity.SetTag("db.inquiry.operation", context.Operation.ToString());
        activity.SetTag("db.inquiry.command_type", context.CommandType.ToString());
        activity.SetTag("db.inquiry.entity", context.EntityType?.FullName);
        activity.SetTag("db.inquiry.provider", context.ProviderName);

        if (_options.IncludeCommandText && context.CommandText is not null)
        {
            activity.SetTag("db.statement", context.CommandText);
            activity.SetTag("db.query.text", context.CommandText);
        }

        if (_options.IncludeParameterNames && context.Parameters.Count > 0)
        {
            activity.SetTag("db.inquiry.parameter_names", string.Join(",", context.Parameters.Keys));
        }

        if (_options.IncludeParameterValues)
        {
            foreach (var parameter in context.Parameters)
            {
                activity.SetTag($"db.query.parameter.{parameter.Key.TrimStart('@', ':', '$')}", parameter.Value);
            }
        }

        _options.Enrich?.Invoke(activity, context);
        if (_options.EnrichWithDbCommand is not null)
        {
            context.CommandEnrichers.Add((ctx, command) => _options.EnrichWithDbCommand(activity, ctx, command));
        }
    }
}
