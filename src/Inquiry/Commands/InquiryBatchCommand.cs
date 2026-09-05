using System.ComponentModel;
using System.Data;
using System.Data.Common;

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
        Action<DbCommand, IReadOnlyList<TItem>>? bindChunk = null,
        bool preferPrepareOnce = false)
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
        CommandTextFactory = null;
        ParametersPerItem = 0;
        MaxItemsPerCommand = int.MaxValue;
        SetBasedMaxItemsPerCommand = int.MaxValue;
        UseChunk = null;
        PreferPrepareOnce = preferPrepareOnce;
        IgnoresMaxBatchSize = false;
    }

    /// <summary>Initializes a whole-chunk generated batch command definition.</summary>
    public InquiryBatchCommand(
        Func<int, string> commandTextFactory,
        Action<DbCommand, IReadOnlyList<TItem>> bindChunk,
        int parametersPerItem,
        int maxItemsPerCommand = int.MaxValue,
        CommandType commandType = CommandType.Text,
        bool ignoresMaxBatchSize = false)
    {
        if (!Enum.IsDefined(commandType))
        {
            throw new ArgumentOutOfRangeException(nameof(commandType), commandType, "Command type is not valid.");
        }

        if (parametersPerItem < 0)
            throw new ArgumentOutOfRangeException(nameof(parametersPerItem), parametersPerItem, "Parameters per item cannot be negative.");
        if (maxItemsPerCommand <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItemsPerCommand), maxItemsPerCommand, "Maximum items per command must be positive.");

        CommandTextFactory = commandTextFactory ?? throw new ArgumentNullException(nameof(commandTextFactory));
        BindChunk = bindChunk ?? throw new ArgumentNullException(nameof(bindChunk));
        CommandType = commandType;
        CommandText = null;
        BindItem = null;
        ParametersPerItem = parametersPerItem;
        MaxItemsPerCommand = maxItemsPerCommand;
        SetBasedMaxItemsPerCommand = maxItemsPerCommand;
        UseChunk = null;
        PreferPrepareOnce = false;
        IgnoresMaxBatchSize = ignoresMaxBatchSize;
    }

    /// <summary>Initializes a generated command that selects a set-based chunk shape when eligible.</summary>
    public InquiryBatchCommand(
        string commandText,
        Action<InquiryParameterTarget, TItem> bindItem,
        Func<int, string> chunkCommandTextFactory,
        Action<DbCommand, IReadOnlyList<TItem>> bindChunk,
        Func<IReadOnlyList<TItem>, bool> useChunk,
        int parametersPerItem,
        int maxItemsPerCommand = int.MaxValue,
        CommandType commandType = CommandType.Text,
        int setBasedMaxItemsPerCommand = int.MaxValue)
        : this(chunkCommandTextFactory, bindChunk, parametersPerItem, maxItemsPerCommand, commandType)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
        if (setBasedMaxItemsPerCommand <= 0)
            throw new ArgumentOutOfRangeException(nameof(setBasedMaxItemsPerCommand), setBasedMaxItemsPerCommand,
                "Maximum set-based items per command must be positive.");
        CommandText = commandText;
        BindItem = bindItem ?? throw new ArgumentNullException(nameof(bindItem));
        UseChunk = useChunk ?? throw new ArgumentNullException(nameof(useChunk));
        SetBasedMaxItemsPerCommand = setBasedMaxItemsPerCommand;
    }

    /// <summary>Gets the fixed-row SQL or stored-procedure name, or null for a whole-chunk-only definition.</summary>
    internal string? CommandText { get; }

    /// <summary>Gets the ADO.NET command type.</summary>
    internal CommandType CommandType { get; }

    /// <summary>Gets the binder invoked for one item.</summary>
    internal Action<InquiryParameterTarget, TItem>? BindItem { get; }

    /// <summary>Gets the optional provider array/whole-chunk binder.</summary>
    internal Action<DbCommand, IReadOnlyList<TItem>>? BindChunk { get; }

    /// <summary>Gets the optional whole-chunk SQL factory.</summary>
    internal Func<int, string>? CommandTextFactory { get; }

    /// <summary>Gets the number of generated parameters contributed by each chunk item.</summary>
    internal int ParametersPerItem { get; }

    /// <summary>Gets the generated dialect/statement row limit.</summary>
    internal int MaxItemsPerCommand { get; }

    /// <summary>Gets the generated provider limit for one set-based command.</summary>
    internal int SetBasedMaxItemsPerCommand { get; }

    /// <summary>Gets the optional per-chunk set-based eligibility selector.</summary>
    internal Func<IReadOnlyList<TItem>, bool>? UseChunk { get; }

    /// <summary>Gets whether the binding streams the entire source natively, bypassing MaxBatchSize chunking.</summary>
    internal bool IgnoresMaxBatchSize { get; }

    /// <summary>Gets whether <see cref="PreparedStatementMode.Auto"/> may prepare the reused command once for this batch.</summary>
    internal bool PreferPrepareOnce { get; }

    internal void Validate()
    {
        if (CommandTextFactory is null && string.IsNullOrWhiteSpace(CommandText))
            throw new ArgumentException("Command text cannot be empty.", "commandText");
        if (CommandTextFactory is null && BindItem is null) throw new ArgumentNullException("bindItem");
        if (CommandTextFactory is not null && BindChunk is null) throw new ArgumentNullException("bindChunk");
        if (ParametersPerItem < 0) throw new ArgumentOutOfRangeException("parametersPerItem");
        if (MaxItemsPerCommand <= 0 && CommandTextFactory is not null) throw new ArgumentOutOfRangeException("maxItemsPerCommand");
        if (SetBasedMaxItemsPerCommand <= 0 && CommandTextFactory is not null)
            throw new ArgumentOutOfRangeException("setBasedMaxItemsPerCommand");
        if (!Enum.IsDefined(CommandType)) throw new ArgumentOutOfRangeException("commandType", CommandType, "Command type is not valid.");
    }

    internal InquiryGeneratedCommand<InquiryBatchItem<TItem>> ForItem(TItem item)
        => new(CommandText!, new InquiryBatchItem<TItem>(this, item), static (dbCommand, state) =>
            state.Command.BindItem!(new InquiryParameterTarget(dbCommand), state.Item), CommandType);

    internal InquiryGeneratedCommand<InquiryBatchChunk<TItem>> ForChunk(IReadOnlyList<TItem> chunk)
        => new(GetChunkCommandText(chunk.Count), new InquiryBatchChunk<TItem>(this, chunk), static (dbCommand, state) =>
            state.Command.BindChunk!(dbCommand, state.Chunk), CommandType);

    internal string GetChunkCommandText(int chunkCount)
    {
        var commandText = CommandTextFactory is null ? CommandText : CommandTextFactory(chunkCount);
        if (string.IsNullOrWhiteSpace(commandText))
            throw new ArgumentException("The batch command text factory returned empty command text.", "commandTextFactory");
        return commandText;
    }

    internal int GetEffectiveChunkSize(int maxBatchSize, int maxParametersPerCommand)
    {
        if (ParametersPerItem > maxParametersPerCommand)
            throw new InvalidOperationException("The configured command parameter limit cannot fit one batch item.");

        var size = IgnoresMaxBatchSize
            ? MaxItemsPerCommand
            : Math.Min(maxBatchSize, MaxItemsPerCommand);
        if (CommandTextFactory is not null && UseChunk is null && ParametersPerItem > 0)
            size = Math.Min(size, maxParametersPerCommand / ParametersPerItem);
        if (size < 1)
            throw new InvalidOperationException("The configured command parameter limit cannot fit one batch item.");
        return size;
    }

    internal bool ShouldUseChunk(IReadOnlyList<TItem> chunk, int maxParametersPerCommand)
    {
        if (UseChunk?.Invoke(chunk) != true) return false;
        var parameterLimit = ParametersPerItem > 0
            ? maxParametersPerCommand / ParametersPerItem
            : int.MaxValue;
        return chunk.Count <= Math.Min(SetBasedMaxItemsPerCommand, parameterLimit);
    }
}

internal readonly record struct InquiryBatchItem<TItem>(InquiryBatchCommand<TItem> Command, TItem Item);
internal readonly record struct InquiryBatchChunk<TItem>(InquiryBatchCommand<TItem> Command, IReadOnlyList<TItem> Chunk);
