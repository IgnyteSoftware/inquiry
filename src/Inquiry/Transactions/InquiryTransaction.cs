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
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private int _closed;
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
            return _terminalTask = CompleteTerminalAsync(lease, commit: true, cancellationToken);
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
            return _terminalTask = CompleteTerminalAsync(lease, commit: false, cancellationToken);
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
                _terminalTask = CompleteDisposalAsync(lease);
                _disposeTask = _terminalTask;
                return new ValueTask(_disposeTask);
            }

            // A successful terminal operation defines the database outcome; only DisposeAsync owns
            // ordinary resource cleanup. A failed terminal operation already performed its bounded
            // rollback/cleanup and owns that aggregate failure, so disposal only observes it.
            _disposeTask = DisposeAfterTerminalAsync(_terminalTask);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task CompleteTerminalAsync(
        TransactedInquiryRequestPipeline.InFlightLease lease,
        bool commit,
        CancellationToken cancellationToken)
    {
        using (lease)
        {
            try
            {
                if (commit)
                {
                    await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception primaryFailure)
            {
                var failures = new List<Exception> { primaryFailure };
                await RollbackAndCleanupBoundedAsync(failures).ConfigureAwait(false);
                ThrowFailures(failures, "The transaction terminal operation failed and provider cleanup also reported errors.");
            }
        }
    }

    private async Task CompleteDisposalAsync(TransactedInquiryRequestPipeline.InFlightLease lease)
    {
        using (lease)
        {
            var failures = new List<Exception>();
            await RollbackAndCleanupBoundedAsync(failures).ConfigureAwait(false);
            ThrowFailures(failures, "Transaction disposal could not complete rollback and provider cleanup.");
        }
    }

    private async Task DisposeAfterTerminalAsync(Task terminalTask)
    {
        try
        {
            await terminalTask.ConfigureAwait(false);
        }
        catch
        {
            // The CommitAsync/RollbackAsync caller owns the primary failure and its cleanup
            // aggregate. Re-observing it from await using would mask that original call site.
            return;
        }

        var failures = new List<Exception>();
        await DisposeProviderResourcesBoundedAsync(failures).ConfigureAwait(false);
        ThrowFailures(failures, "The database transaction completed successfully, but provider resource cleanup failed.");
    }

    private async Task RollbackAndCleanupBoundedAsync(List<Exception> failures)
    {
        Task rollbackTask;
        try
        {
            rollbackTask = _transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            await DisposeProviderResourcesBoundedAsync(failures).ConfigureAwait(false);
            return;
        }

        try
        {
            await rollbackTask.WaitAsync(CleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            failures.Add(new TimeoutException(
                $"Provider rollback did not complete within {CleanupTimeout.TotalSeconds:0} seconds; cleanup will continue sequentially in the background."));
            _ = ContinueCleanupAfterRollbackAsync(rollbackTask);
            return;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await DisposeProviderResourcesBoundedAsync(failures).ConfigureAwait(false);
    }

    private async Task DisposeProviderResourcesBoundedAsync(List<Exception> failures)
    {
        Task transactionDisposeTask;
        try
        {
            transactionDisposeTask = _transaction.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            await DisposeConnectionBoundedAsync(failures).ConfigureAwait(false);
            return;
        }

        try
        {
            await transactionDisposeTask.WaitAsync(CleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            failures.Add(new TimeoutException(
                $"Provider transaction disposal did not complete within {CleanupTimeout.TotalSeconds:0} seconds; connection cleanup will continue sequentially in the background."));
            _ = ContinueConnectionCleanupAsync(transactionDisposeTask);
            return;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await DisposeConnectionBoundedAsync(failures).ConfigureAwait(false);
    }

    private async Task DisposeConnectionBoundedAsync(List<Exception> failures)
    {
        Task connectionDisposeTask;
        try
        {
            connectionDisposeTask = _connection.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return;
        }

        try
        {
            await connectionDisposeTask.WaitAsync(CleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            failures.Add(new TimeoutException(
                $"Provider connection disposal did not complete within {CleanupTimeout.TotalSeconds:0} seconds; it will continue in the background."));
            _ = ObserveBackgroundAsync(connectionDisposeTask);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async Task ContinueCleanupAfterRollbackAsync(Task rollbackTask)
    {
        try { await rollbackTask.ConfigureAwait(false); }
        catch { }
        await DisposeProviderResourcesUnboundedObservedAsync().ConfigureAwait(false);
    }

    private async Task ContinueConnectionCleanupAsync(Task transactionDisposeTask)
    {
        try { await transactionDisposeTask.ConfigureAwait(false); }
        catch { }
        try { await _connection.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    private async Task DisposeProviderResourcesUnboundedObservedAsync()
    {
        try { await _transaction.DisposeAsync().ConfigureAwait(false); }
        catch { }
        try { await _connection.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    private static async Task ObserveBackgroundAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { }
    }

    private static void ThrowFailures(List<Exception> failures, string aggregateMessage)
    {
        if (failures.Count == 0) return;
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        throw new AggregateException(aggregateMessage, failures);
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
