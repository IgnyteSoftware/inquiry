using System.Data;
using System.Data.Common;
using Inquiry.Pipeline;

namespace Inquiry.Transactions;

/// <summary>
/// Default implementation of <see cref="IInquiryTransaction"/>.
/// Rolls back automatically on disposal unless <see cref="CommitAsync"/> has been called.
/// </summary>
internal sealed class InquiryTransaction : InquiryTransactionBase
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly TransactedInquiryRequestPipeline _pipeline;
    private readonly Action _onDetach;
    private readonly Action _onClose;
    private readonly object _lifecycleLock = new();
    private int _closed;
    private bool _terminalSucceeded;
    private Task? _terminalTask;
    private Task? _disposeTask;

    internal InquiryTransaction(
        DbConnection connection,
        DbTransaction transaction,
        TransactedInquiryRequestPipeline pipeline,
        IInquiry inquiry,
        Action onDetach,
        Action onClose)
        : base(inquiry)
    {
        _connection = connection;
        _transaction = transaction;
        _pipeline = pipeline;
        _onDetach = onDetach;
        _onClose = onClose;
    }

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _transaction.IsolationLevel;

    /// <inheritdoc />
    public override DbConnection Connection
    {
        get
        {
            ThrowIfClosed();
            return _connection;
        }
    }

    /// <inheritdoc />
    public override DbTransaction Transaction
    {
        get
        {
            ThrowIfClosed();
            return _transaction;
        }
    }

    /// <inheritdoc />
    public override void ThrowIfClosed()
    {
        // _closed is atomically set when a commit, rollback, or disposal wins terminal ownership.
        if (System.Threading.Volatile.Read(ref _closed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(InquiryTransaction),
                "This Inquiry transaction has already been committed, rolled back, or disposed. " +
                "Calls routed through the transaction handle (tx.X) after close are not allowed.");
        }
    }

    /// <inheritdoc />
    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            ThrowIfClosed();
            var lease = _pipeline.EnterTerminalOperation(); // fail-fast while busy, without poisoning the handle
            _onDetach();
            Close();
            return _terminalTask = CommitCoreAsync(lease, cancellationToken);
        }
    }

    /// <inheritdoc />
    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            ThrowIfClosed();
            var lease = _pipeline.EnterTerminalOperation();
            _onDetach();
            Close();
            return _terminalTask = RollbackCoreAsync(lease, cancellationToken);
        }
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);

            if (_terminalTask is null)
            {
                var lease = _pipeline.EnterTerminalOperation(); // busy disposal is rejected and retryable
                _onDetach();
                Close();
                lease.Dispose();
            }

            _disposeTask = DisposeAfterTerminalAsync(_terminalTask);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task CommitCoreAsync(TransactedInquiryRequestPipeline.InFlightLease lease, CancellationToken cancellationToken)
    {
        using (lease)
        {
            try
            {
                await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _terminalSucceeded = true;
            }
            finally
            {
                // Whether the underlying CommitAsync succeeded or threw, this handle is now finished:
                // the captured slot must be closed (so straggler ambient store calls fail fast), and
                // _closed must be set (so direct tx.X(...) calls trip ThrowIfClosed instead of
                // silently operating on a corrupted transaction). If commit threw, terminal success
                // remains false and DisposeAsync performs one best-effort rollback during cleanup.
                Close();
            }
        }
    }

    private async Task RollbackCoreAsync(TransactedInquiryRequestPipeline.InFlightLease lease, CancellationToken cancellationToken)
    {
        using (lease)
        {
            try
            {
                await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _terminalSucceeded = true;
            }
            finally
            {
                // Same as CommitAsync: even if the underlying RollbackAsync threw, the handle is done.
                Close();
            }
        }
    }

    private async Task DisposeAfterTerminalAsync(Task? terminalTask)
    {
        if (terminalTask is not null)
        {
            try { await terminalTask.ConfigureAwait(false); }
            catch { /* terminal caller owns its exception; disposal still performs cleanup */ }
        }

        if (!_terminalSucceeded)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Rollback on dispose is best-effort; swallow to avoid masking real exceptions.
            }
        }

        Exception? transactionDisposeFailure = null;
        try { await _transaction.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { transactionDisposeFailure = exception; }

        try { await _connection.DisposeAsync().ConfigureAwait(false); }
        catch when (transactionDisposeFailure is not null)
        {
            // The transaction-dispose failure is primary, but connection cleanup was still attempted.
        }

        if (transactionDisposeFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(transactionDisposeFailure).Throw();
    }

    /// <summary>
    /// Closes the captured ambient transactional slot (idempotent). Called on the first of
    /// Commit / Rollback / Dispose so that any straggler async work that runs after this
    /// transaction is closed fails fast instead of falling through to the default pipeline.
    /// </summary>
    private void Close()
    {
        if (System.Threading.Interlocked.Exchange(ref _closed, 1) != 0) return;
        _pipeline.MarkClosed();
        _onClose();
    }
}
