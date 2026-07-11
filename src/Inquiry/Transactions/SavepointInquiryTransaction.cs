using Inquiry.Pipeline;
using System.Data;
using System.Data.Common;

namespace Inquiry.Transactions;

/// <summary>
/// An <see cref="IInquiryTransaction"/> backed by a savepoint inside an outer transaction.
/// Created when <see cref="IInquiry.BeginTransactionAsync"/> is called while an ambient
/// transaction is already active — the call doesn't start a new physical transaction; it
/// emits <c>SAVEPOINT</c> on the existing one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CommitAsync"/> emits <c>RELEASE SAVEPOINT</c>. On Oracle (where savepoints
/// cannot be explicitly released and are instead implicitly released on outer commit /
/// rollback / a later <c>ROLLBACK TO</c>) the call falls through silently — the savepoint
/// will be cleaned up by the outer transaction's lifecycle.
/// </para>
/// <para>
/// <see cref="RollbackAsync"/> emits <c>ROLLBACK TO SAVEPOINT</c>, which reverts everything
/// done since the savepoint was created without affecting the outer transaction.
/// </para>
/// <para>
/// <see cref="DisposeAsync"/> best-effort rolls back if neither <see cref="CommitAsync"/>
/// nor <see cref="RollbackAsync"/> was called. Any exception is swallowed so a failing
/// dispose can't mask a real exception from the user's code.
/// </para>
/// <para>
/// Operations on this handle route through the same ambient pipeline as the outer
/// transaction — same connection, same transaction — but the closed-state check on the
/// savepoint handle prevents calls after the savepoint has been released or rolled back.
/// </para>
/// </remarks>
internal sealed class SavepointInquiryTransaction : InquiryTransactionBase
{
    private readonly TransactedInquiryRequestPipeline _outerPipeline;
    private readonly string _savepointName;
    private readonly IsolationLevel _isolationLevel;
    private readonly object _lifecycleLock = new();
    private bool _closed;
    private bool _terminalSucceeded;
    private bool _cleanupAttempted;
    private Task? _terminalTask;
    private Task? _disposeTask;

    internal SavepointInquiryTransaction(
        IInquiry inquiry,
        TransactedInquiryRequestPipeline outerPipeline,
        string savepointName,
        IsolationLevel isolationLevel)
        : base(inquiry)
    {
        _outerPipeline = outerPipeline;
        _savepointName = savepointName;
        _isolationLevel = isolationLevel;
    }

    /// <inheritdoc />
    public override IsolationLevel IsolationLevel => _isolationLevel;

    /// <inheritdoc />
    public override DbConnection Connection
    {
        get
        {
            ThrowIfClosed();
            return _outerPipeline.Connection;
        }
    }

    /// <inheritdoc />
    public override DbTransaction Transaction
    {
        get
        {
            ThrowIfClosed();
            return _outerPipeline.Transaction;
        }
    }

    /// <inheritdoc />
    public override void ThrowIfClosed()
    {
        // _closed is set after a successful Commit or Rollback; _committed implies _closed
        // (kept separately so Dispose can distinguish "released" from "rolled back at exit").
        // _disposed is set by DisposeAsync. The outer pipeline's IsClosed covers out-of-order
        // teardown (outer committed/rolled back/disposed while this savepoint handle is still
        // held) — without it the Connection/Transaction interop getters would hand out a
        // disposed pair instead of failing fast. Any of these terminal states blocks forwarding.
        if (_closed || _outerPipeline.IsClosed)
        {
            throw new ObjectDisposedException(
                nameof(SavepointInquiryTransaction),
                "This Inquiry savepoint has already been committed, rolled back, or disposed. " +
                "Calls routed through the savepoint handle (tx.X) after close are not allowed.");
        }
    }

    /// <inheritdoc />
    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            ThrowIfClosed();
            var lease = _outerPipeline.EnterExclusiveOperation();
            _closed = true;
            return _terminalTask = CommitCoreAsync(lease, cancellationToken);
        }
    }

    /// <inheritdoc />
    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            ThrowIfClosed();
            var lease = _outerPipeline.EnterExclusiveOperation();
            _closed = true;
            return _terminalTask = RollbackCoreAsync(lease, cancellationToken);
        }
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            if (_outerPipeline.IsClosed)
            {
                _closed = true;
                _disposeTask = Task.CompletedTask;
                return new ValueTask(_disposeTask);
            }
            if (_terminalTask is null)
            {
                var lease = _outerPipeline.EnterExclusiveOperation(); // acquire before mutation: busy is retryable
                _closed = true;
                _disposeTask = DisposeActiveCoreAsync(lease);
            }
            else
            {
                _disposeTask = DisposeAfterTerminalAsync(_terminalTask);
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task CommitCoreAsync(TransactedInquiryRequestPipeline.InFlightLease lease, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await _outerPipeline.ReleaseSavepointAsync(_savepointName, lease, cancellationToken).ConfigureAwait(false);
                _terminalSucceeded = true;
            }
            catch (NotSupportedException)
            {
                _terminalSucceeded = true;
            }
            catch
            {
                await CleanupWithOwnedLeaseAsync(lease).ConfigureAwait(false);
                throw;
            }
        }
        finally { lease.Dispose(); }
    }

    private async Task RollbackCoreAsync(TransactedInquiryRequestPipeline.InFlightLease lease, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await _outerPipeline.RollbackToSavepointAsync(_savepointName, lease, cancellationToken).ConfigureAwait(false);
                _terminalSucceeded = true;
            }
            catch
            {
                await CleanupWithOwnedLeaseAsync(lease).ConfigureAwait(false);
                throw;
            }
        }
        finally { lease.Dispose(); }
    }

    private async Task DisposeActiveCoreAsync(TransactedInquiryRequestPipeline.InFlightLease lease)
    {
        try { await CleanupWithOwnedLeaseAsync(lease).ConfigureAwait(false); }
        finally { lease.Dispose(); }
    }

    private async Task DisposeAfterTerminalAsync(Task terminalTask)
    {
        try { await terminalTask.ConfigureAwait(false); } catch { }
        if (_terminalSucceeded || _cleanupAttempted || _outerPipeline.IsClosed) return;
        try
        {
            using var lease = _outerPipeline.EnterExclusiveOperation();
            await CleanupWithOwnedLeaseAsync(lease).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task CleanupWithOwnedLeaseAsync(TransactedInquiryRequestPipeline.InFlightLease lease)
    {
        _cleanupAttempted = true;
        try
        {
            await _outerPipeline.RollbackToSavepointAsync(_savepointName, lease, CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
    }
}
