using System.Data;
using System.Data.Common;
using Inquiry.Commands;
using Inquiry.Diagnostics;
using Inquiry.Interceptors;

namespace Inquiry.Pipeline;

/// <summary>
/// Runs the registered command interceptors for one multi-result (grid) command. The pipeline raises
/// the initialized and executing callbacks; the grid raises executed on disposal or failed on the
/// first failure, so the callbacks cover the same lifetime as the grid's QUERY_MULTIPLE span.
/// </summary>
internal sealed class GridCommandInterception
{
    private readonly IInquiryCommandInterceptor[] _interceptors;
    private readonly InquiryCommand _command;
    private readonly DbCommand _dbCommand;

    private GridCommandInterception(IInquiryCommandInterceptor[] interceptors, InquiryCommand command, DbCommand dbCommand)
    {
        _interceptors = interceptors;
        _command = command;
        _dbCommand = dbCommand;
    }

    /// <summary>True after the failed callbacks ran. The grid then skips the executed callbacks.</summary>
    internal bool HasFailed { get; private set; }

    internal static GridCommandInterception? Create(IInquiryCommandInterceptor[] interceptors, DbCommand dbCommand, InquiryCommand command)
    {
        var selected = Select(interceptors);
        return selected is null ? null : new GridCommandInterception(selected, command, dbCommand);
    }

    internal static GridCommandInterception? Create(IInquiryCommandInterceptor[] interceptors, DbCommand dbCommand, string commandText, CommandType commandType)
    {
        var selected = Select(interceptors);
        return selected is null ? null : new GridCommandInterception(selected, new InquiryCommand(commandText, commandType), dbCommand);
    }

    // The grid records its own QUERY_MULTIPLE span and duration metric. The built-in telemetry
    // interceptor is skipped here, because it would record each grid a second time.
    private static IInquiryCommandInterceptor[]? Select(IInquiryCommandInterceptor[] interceptors)
    {
        var count = 0;
        foreach (var interceptor in interceptors)
        {
            if (interceptor is not InquiryTelemetryInterceptor)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        if (count == interceptors.Length)
        {
            return interceptors;
        }

        var selected = new IInquiryCommandInterceptor[count];
        var index = 0;
        foreach (var interceptor in interceptors)
        {
            if (interceptor is not InquiryTelemetryInterceptor)
            {
                selected[index++] = interceptor;
            }
        }

        return selected;
    }

    internal async ValueTask ExecutingAsync(CancellationToken cancellationToken)
    {
        var initialized = new InquiryCommandContext(_command, _dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandInitializedAsync(initialized, cancellationToken).ConfigureAwait(false);
        }

        var executing = new InquiryCommandContext(_command, _dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutingAsync(executing, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async ValueTask ExecutedAsync(CancellationToken cancellationToken)
    {
        var context = new InquiryCommandExecutedContext(_command, _dbCommand, recordsAffected: null);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async ValueTask FailedAsync(Exception exception, CancellationToken cancellationToken)
    {
        // A grid can fail on any read after the command executed. Only the first failure is reported.
        if (HasFailed)
        {
            return;
        }

        HasFailed = true;
        var context = new InquiryCommandFailedContext(_command, _dbCommand, exception);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandFailedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
