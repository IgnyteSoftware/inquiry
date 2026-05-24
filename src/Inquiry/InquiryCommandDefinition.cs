using System.Data;
using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Describes a database command that can be executed by the Inquiry request pipeline.
/// </summary>
public sealed class InquiryCommandDefinition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommandDefinition"/> class.
    /// </summary>
    public InquiryCommandDefinition(
        string commandText,
        Action<DbCommand>? bindParameters = null,
        CommandType? commandType = null,
        int? commandTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
        }

        CommandText = commandText;
        BindParameters = bindParameters;
        CommandType = commandType;
        CommandTimeout = commandTimeout;
    }

    /// <summary>
    /// Gets the SQL command text.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets the delegate used to bind provider-specific parameters.
    /// </summary>
    public Action<DbCommand>? BindParameters { get; }

    /// <summary>
    /// Gets the optional ADO.NET command type.
    /// </summary>
    public CommandType? CommandType { get; }

    /// <summary>
    /// Gets the optional command timeout in seconds.
    /// </summary>
    public int? CommandTimeout { get; }
}
