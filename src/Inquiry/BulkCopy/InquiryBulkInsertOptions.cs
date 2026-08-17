namespace Inquiry.BulkCopy;

/// <summary>Per-call controls for a provider-native bulk insert.</summary>
public sealed class InquiryBulkInsertOptions
{
    /// <summary>Maximum duration of the provider copy operation, or null for its default.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Number of rows per provider batch, or null for its default.</summary>
    public int? BatchSize { get; set; }

    /// <summary>Whether the provider should take a table-level bulk-copy lock.</summary>
    public bool TableLock { get; set; }

    /// <summary>Number of copied rows between progress notifications, or null to disable them.</summary>
    public int? NotifyAfter { get; set; }

    /// <summary>Callback invoked with the cumulative copied-row count at each notification.</summary>
    public Action<long>? RowsCopied { get; set; }

    /// <summary>Controls whether the call requires an ambient or dedicated connection.</summary>
    public InquiryBulkInsertConnectionBehavior ConnectionBehavior { get; set; }

    internal void Validate()
    {
        if (Timeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalSeconds > int.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive and no greater than Int32.MaxValue seconds.");
        if (BatchSize is <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "BatchSize must be positive.");
        if (NotifyAfter is <= 0)
            throw new ArgumentOutOfRangeException(nameof(NotifyAfter), "NotifyAfter must be positive.");
        if (RowsCopied is not null && NotifyAfter is null)
            throw new InvalidOperationException("RowsCopied requires NotifyAfter to be specified.");
        if (!Enum.IsDefined(ConnectionBehavior))
            throw new ArgumentOutOfRangeException(nameof(ConnectionBehavior));
    }
}

/// <summary>Controls which connection a provider-native bulk insert may use.</summary>
public enum InquiryBulkInsertConnectionBehavior
{
    /// <summary>Use the ambient Inquiry transaction when present; otherwise open a dedicated connection.</summary>
    Automatic,

    /// <summary>Require an active ambient Inquiry transaction and fail before copying if none exists.</summary>
    RequireAmbientTransaction,

    /// <summary>Require a dedicated connection and fail if an ambient Inquiry transaction is active.</summary>
    RequireDedicatedConnection,
}
