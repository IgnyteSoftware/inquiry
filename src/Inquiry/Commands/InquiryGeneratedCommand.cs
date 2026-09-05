using System.ComponentModel;
using System.Data;
using System.Data.Common;

namespace Inquiry.Commands;

/// <summary>
/// Immutable command definition used by generated stores to carry value-state and a static
/// parameter binder without allocating an <see cref="InquiryCommand"/>.
/// </summary>
/// <typeparam name="TArgs">The value-state consumed by the generated parameter binder.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct InquiryGeneratedCommand<TArgs>
{
    /// <summary>Initializes a generated command definition.</summary>
    public InquiryGeneratedCommand(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CommandType commandType = CommandType.Text)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
        }

        CommandText = commandText;
        Args = args;
        BindParameters = bindParameters ?? throw new ArgumentNullException(nameof(bindParameters));
        CommandType = commandType;
    }

    /// <summary>Gets the SQL or stored-procedure name.</summary>
    internal string CommandText { get; }

    /// <summary>Gets the ADO.NET command type.</summary>
    internal CommandType CommandType { get; }

    /// <summary>Gets the binder value-state.</summary>
    internal TArgs Args { get; }

    /// <summary>Gets the static parameter binder.</summary>
    internal Action<DbCommand, TArgs> BindParameters { get; }

    internal InquiryCommand ToInquiryCommand()
    {
        Validate();
        var args = Args;
        var bindParameters = BindParameters;
        return new InquiryCommand(CommandText, command => bindParameters(command, args), CommandType);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(CommandText))
        {
            throw new ArgumentException("Command text cannot be empty.", "commandText");
        }

        if (BindParameters is null)
        {
            throw new ArgumentNullException("bindParameters");
        }
    }
}
