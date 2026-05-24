using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Provides command context to Inquiry command interceptors.
/// </summary>
public class InquiryCommandContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommandContext"/> class.
    /// </summary>
    public InquiryCommandContext(InquiryCommandDefinition commandDefinition, DbCommand command)
    {
        CommandDefinition = commandDefinition ?? throw new ArgumentNullException(nameof(commandDefinition));
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Gets the command definition used to configure the command.
    /// </summary>
    public InquiryCommandDefinition CommandDefinition { get; }

    /// <summary>
    /// Gets the mutable ADO.NET command.
    /// </summary>
    public DbCommand Command { get; }
}
