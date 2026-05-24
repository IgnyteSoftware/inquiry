using Inquiry.Commands;

namespace Inquiry.Interceptors;

/// <summary>
/// Observes and mutates commands executed by the Inquiry request pipeline.
/// </summary>
public interface IInquiryCommandInterceptor
{
    /// <summary>
    /// Called after command text and parameters are applied.
    /// </summary>
    ValueTask CommandInitializedAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Called immediately before command execution.
    /// </summary>
    ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Called after successful command execution.
    /// </summary>
    ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Called after command execution fails and before the exception is rethrown.
    /// </summary>
    ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
