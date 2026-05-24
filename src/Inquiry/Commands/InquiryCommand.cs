using Inquiry.Parameters;
using System.Data;

namespace Inquiry.Commands;

/// <summary>
/// Describes a database command that can be executed by the Inquiry request pipeline.
/// </summary>
public sealed class InquiryCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommand"/> class.
    /// </summary>
    public InquiryCommand(string commandText, CommandType? commandType = null, int? commandTimeout = null)
        : this(commandText, Array.Empty<InquiryParameter>(), commandType, commandTimeout)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommand"/> class.
    /// </summary>
    public InquiryCommand(
        string commandText,
        IReadOnlyList<InquiryParameter> parameters,
        CommandType? commandType = null,
        int? commandTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
        }

        if (commandTimeout is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout), commandTimeout, "Command timeout cannot be negative.");
        }

        CommandText = commandText;
        Parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));
        CommandType = commandType;
        CommandTimeout = commandTimeout;
    }

    /// <summary>
    /// Gets the SQL command text.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets the parameters to bind to the command.
    /// </summary>
    public IReadOnlyList<InquiryParameter> Parameters { get; }

    /// <summary>
    /// Gets the optional ADO.NET command type.
    /// </summary>
    public CommandType? CommandType { get; }

    /// <summary>
    /// Gets the optional command timeout in seconds.
    /// </summary>
    public int? CommandTimeout { get; }
}
