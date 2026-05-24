using System.Data.Common;

namespace Inquiry.Commands;

/// <summary>
/// Provides command context to Inquiry command interceptors.
/// </summary>
public class InquiryCommandContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommandContext"/> class.
    /// </summary>
    public InquiryCommandContext(InquiryCommand commandDefinition, DbCommand command)
    {
        InquiryCommand = commandDefinition ?? throw new ArgumentNullException(nameof(commandDefinition));
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Gets the command definition used to configure the command.
    /// </summary>
    public InquiryCommand InquiryCommand { get; }

    /// <summary>
    /// Gets the mutable ADO.NET command.
    /// </summary>
    public DbCommand Command { get; }
}
