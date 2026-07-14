using System.ComponentModel;
using System.Data;

namespace Inquiry.Commands;

/// <summary>Immutable generated-code definition for one batch mutation command.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct InquiryBatchCommand<TItem>
{
    /// <summary>Initializes a generated batch command definition.</summary>
    public InquiryBatchCommand(
        string commandText,
        Action<InquiryParameterTarget, TItem> bindItem,
        CommandType commandType = CommandType.Text,
        Action<InquiryParameterTarget, IReadOnlyList<TItem>>? bindChunk = null)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
        }

        if (!Enum.IsDefined(commandType))
        {
            throw new ArgumentOutOfRangeException(nameof(commandType), commandType, "Command type is not valid.");
        }

        CommandText = commandText;
        BindItem = bindItem ?? throw new ArgumentNullException(nameof(bindItem));
        CommandType = commandType;
        BindChunk = bindChunk;
    }

    /// <summary>Gets the constant SQL or stored-procedure name.</summary>
    public string CommandText { get; }

    /// <summary>Gets the ADO.NET command type.</summary>
    public CommandType CommandType { get; }

    /// <summary>Gets the binder invoked for one item.</summary>
    public Action<InquiryParameterTarget, TItem> BindItem { get; }

    /// <summary>Gets the optional provider array/whole-chunk binder.</summary>
    public Action<InquiryParameterTarget, IReadOnlyList<TItem>>? BindChunk { get; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(CommandText)) throw new ArgumentException("Command text cannot be empty.", "commandText");
        if (BindItem is null) throw new ArgumentNullException("bindItem");
        if (!Enum.IsDefined(CommandType)) throw new ArgumentOutOfRangeException("commandType", CommandType, "Command type is not valid.");
    }
}
