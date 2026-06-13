using Inquiry.Commands;
using System.Diagnostics;
using System.Text;

namespace Inquiry.Interceptors;

/// <summary>
/// Appends a <see href="https://google.github.io/sqlcommenter/">sqlcommenter</see>-style comment to
/// each command's text — <c>/*application='api',traceparent='00-…-…-01'*/</c> — so database-side
/// tooling (slow-query logs, <c>pg_stat_activity</c>, DBA traces) can be correlated back to the
/// distributed trace that issued the statement. The <c>traceparent</c> value comes from
/// <see cref="Activity.Current"/> (W3C format); when no activity is recording and no static tags
/// are configured, the command is left untouched.
/// </summary>
/// <remarks>
/// Trace ids change per request, so tagged command text varies per execution — a tagged command
/// defeats server-side prepared-statement reuse for that statement. Enable it where DBA-side
/// correlation matters more than prepared reuse, or scope it to diagnosis sessions; see the
/// Interceptors article for the trade-off.
/// </remarks>
public sealed class SqlCommenterInterceptor : IInquiryCommandInterceptor
{
    private readonly string? _staticFragment;

    /// <summary>Initializes the interceptor.</summary>
    /// <param name="applicationName">
    /// Optional static <c>application</c> tag value included in every comment (e.g. the service name).
    /// </param>
    public SqlCommenterInterceptor(string? applicationName = null)
    {
        _staticFragment = string.IsNullOrWhiteSpace(applicationName)
            ? null
            : "application='" + Escape(applicationName!) + "'";
    }

    /// <inheritdoc />
    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var text = context.Command.CommandText;
        if (text.Length == 0 || text.Contains("/*", StringComparison.Ordinal))
        {
            // Already tagged (retry through the same interceptor chain) or hand-tagged — skip.
            return ValueTask.CompletedTask;
        }

        var activity = Activity.Current;
        var traceparent = activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
        if (traceparent is null && _staticFragment is null)
        {
            return ValueTask.CompletedTask;
        }

        // sqlcommenter format: keys sorted, values single-quoted, comment appended after the text.
        var comment = new StringBuilder(text.Length + 96).Append(text).Append(" /*");
        if (_staticFragment is not null)
        {
            comment.Append(_staticFragment);
            if (traceparent is not null)
            {
                comment.Append(',');
            }
        }

        if (traceparent is not null)
        {
            comment.Append("traceparent='").Append(Escape(traceparent)).Append('\'');
        }

        context.Command.CommandText = comment.Append("*/").ToString();
        return ValueTask.CompletedTask;
    }

    private static string Escape(string value)
        // The sqlcommenter spec URL-encodes values before single-quoting them, which is also what
        // makes the comment safe to embed: quotes become %27 and '/' becomes %2F, so neither the
        // value quoting nor the surrounding SQL comment can be terminated by tag content.
        => Uri.EscapeDataString(value);
}
