using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Provides successful command execution details to Inquiry command interceptors.
/// </summary>
public sealed class InquiryCommandExecutedContext : InquiryCommandContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommandExecutedContext"/> class.
    /// </summary>
    public InquiryCommandExecutedContext(InquiryCommandDefinition commandDefinition, DbCommand command, int? recordsAffected)
        : base(commandDefinition, command)
    {
        RecordsAffected = recordsAffected;
    }

    /// <summary>
    /// Gets the affected row count when available.
    /// </summary>
    public int? RecordsAffected { get; }
}
