using Inquiry.Parameters;
using System.Data;

namespace Inquiry.Commands;

/// <summary>
/// Describes a database command that can be executed by the Inquiry request pipeline.
/// </summary>
public sealed class InquiryCommand
{
    private readonly InquiryParameter[] _parameters;

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
        _parameters = parameters switch
        {
            null => throw new ArgumentNullException(nameof(parameters)),
            InquiryParameter[] array => array,
            _ => parameters.ToArray(),
        };
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
    public IReadOnlyList<InquiryParameter> Parameters => _parameters;

    /// <summary>
    /// Internal accessor used by the pipeline binder to iterate the parameters as a strongly-typed
    /// array — index-based, no enumerator boxing.
    /// </summary>
    internal InquiryParameter[] ParametersArray => _parameters;

    /// <summary>
    /// Gets the optional ADO.NET command type.
    /// </summary>
    public CommandType? CommandType { get; }

    /// <summary>
    /// Gets the optional command timeout in seconds.
    /// </summary>
    public int? CommandTimeout { get; }
}
