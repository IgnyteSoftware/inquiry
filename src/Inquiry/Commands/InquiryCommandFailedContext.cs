using System.Data.Common;

namespace Inquiry.Commands;

/// <summary>
/// Provides failed command execution details to Inquiry command interceptors.
/// </summary>
public sealed class InquiryCommandFailedContext : InquiryCommandContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommandFailedContext"/> class.
    /// </summary>
    public InquiryCommandFailedContext(InquiryCommandDefinition commandDefinition, DbCommand command, Exception exception)
        : base(commandDefinition, command)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    /// <summary>
    /// Gets the exception that will be rethrown by the pipeline.
    /// </summary>
    public Exception Exception { get; }
}
