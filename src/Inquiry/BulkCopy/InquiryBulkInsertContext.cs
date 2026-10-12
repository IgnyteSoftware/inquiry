using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;

namespace Inquiry.BulkCopy;

/// <summary>Connection, transaction, options, and telemetry callbacks for a native bulk insert.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryBulkInsertContext
{
    private readonly Action<TimeSpan, Exception?>? _connectionOpened;
    private readonly Action<TimeSpan, long?>? _copyCompleted;

    internal InquiryBulkInsertContext(
        DbConnection? connection,
        DbTransaction? transaction,
        InquiryBulkInsertOptions options,
        Action<TimeSpan, Exception?>? connectionOpened,
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

    internal void RecordConnectionOpened(TimeSpan duration) => _connectionOpened?.Invoke(duration, null);

    internal void RecordCopyCompleted(TimeSpan duration, long? rowCount = null) => _copyCompleted?.Invoke(duration, rowCount);

    /// <summary>
    /// Opens a dedicated connection and records the open phase. A failed open is recorded as a failure,
    /// and its exception propagates unchanged. The caller owns and disposes the returned connection.
    /// </summary>
    internal async ValueTask<DbConnection> OpenDedicatedConnectionAsync(
        Func<CancellationToken, ValueTask<DbConnection>> open,
        CancellationToken cancellationToken)
    {
        var openTimestamp = Stopwatch.GetTimestamp();
        DbConnection connection;
        try
        {
            connection = await open(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            try
            {
                _connectionOpened?.Invoke(Stopwatch.GetElapsedTime(openTimestamp), exception);
            }
            catch
            {
                // The open failure is the outcome. A failing telemetry listener must not replace it.
            }

            throw;
        }

        // Telemetry listeners can run user code. If one throws, dispose the new connection so it does not leak.
        try
        {
            RecordConnectionOpened(Stopwatch.GetElapsedTime(openTimestamp));
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }
}
