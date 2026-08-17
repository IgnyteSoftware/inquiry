using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.BulkCopy;

/// <summary>Connection, transaction, options, and telemetry callbacks for a native bulk insert.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryBulkInsertContext
{
    private readonly Action<TimeSpan>? _connectionOpened;
    private readonly Action<TimeSpan, long?>? _copyCompleted;

    internal InquiryBulkInsertContext(
        DbConnection? connection,
        DbTransaction? transaction,
        InquiryBulkInsertOptions options,
        Action<TimeSpan>? connectionOpened,
        Action<TimeSpan, long?>? copyCompleted)
    {
        Connection = connection;
        Transaction = transaction;
        Options = options;
        _connectionOpened = connectionOpened;
        _copyCompleted = copyCompleted;
    }

    /// <summary>The caller-owned ambient connection, or null when a dedicated connection is required.</summary>
    public DbConnection? Connection { get; }

    /// <summary>The caller-owned ambient transaction, or null when a dedicated connection is required.</summary>
    public DbTransaction? Transaction { get; }

    /// <summary>The validated per-call options.</summary>
    public InquiryBulkInsertOptions Options { get; }

    /// <summary>True when the copy must use the caller-owned ambient transaction.</summary>
    public bool IsEnlisted => Transaction is not null;

    internal void RecordConnectionOpened(TimeSpan duration) => _connectionOpened?.Invoke(duration);

    internal void RecordCopyCompleted(TimeSpan duration, long? rowCount = null) => _copyCompleted?.Invoke(duration, rowCount);
}
