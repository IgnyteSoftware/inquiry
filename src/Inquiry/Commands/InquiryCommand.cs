using Inquiry.Parameters;
using System.Data;
using System.Data.Common;

namespace Inquiry.Commands;

/// <summary>
/// Describes a database command that can be executed by the Inquiry request pipeline.
/// </summary>
public sealed class InquiryCommand
{
    private readonly InquiryParameter[] _parameters;
    private readonly IReadOnlyList<InquiryParameter> _parameterView;

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
        ArgumentNullException.ThrowIfNull(parameters);
        _parameters = parameters.Count == 0 ? Array.Empty<InquiryParameter>() : parameters.ToArray();
        _parameterView = _parameters.Length == 0 ? Array.Empty<InquiryParameter>() : Array.AsReadOnly(_parameters);
        CommandType = commandType;
        CommandTimeout = commandTimeout;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryCommand"/> class that carries an
    /// optional direct <see cref="DbCommand"/> binder. The pipeline invokes
    /// <paramref name="dbCommandBinder"/> after applying the <see cref="Parameters"/> array, so
    /// callers can write straight into <c>DbCommand.Parameters</c> without going through the
    /// <see cref="InquiryParameter"/> intermediate. Used by the default interface implementations
    /// of the fast-path <c>ExecuteAsync&lt;TArgs&gt;</c> overload to bridge custom pipelines onto
    /// the existing <see cref="InquiryCommand"/>-based path.
    /// </summary>
    public InquiryCommand(
        string commandText,
        Action<DbCommand> dbCommandBinder,
        CommandType? commandType = null,
        int? commandTimeout = null)
        : this(commandText, Array.Empty<InquiryParameter>(), commandType, commandTimeout)
    {
        DbCommandBinder = dbCommandBinder ?? throw new ArgumentNullException(nameof(dbCommandBinder));
    }

    /// <summary>
    /// Gets the SQL command text.
    /// </summary>
    public string CommandText { get; }

    /// <summary>
    /// Gets a read-only shallow snapshot of the parameters supplied at construction.
    /// Replacing items in the caller's collection does not change this command. Parameter values
    /// are not deep-copied; mutable values and binder-captured state remain the caller's responsibility.
    /// </summary>
    public IReadOnlyList<InquiryParameter> Parameters => _parameterView;

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

    /// <summary>
    /// Gets the optional callback that writes parameters straight into a <see cref="DbCommand"/>
    /// after <see cref="Parameters"/> has been applied. Non-null only for commands constructed via
    /// the <see cref="InquiryCommand(string, Action{DbCommand}, CommandType?, int?)"/> overload.
    /// </summary>
    public Action<DbCommand>? DbCommandBinder { get; }
}
