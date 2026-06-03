using Inquiry.Pipeline;
using System.Data;

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
    private bool _closed;
    private bool _committed;
    private bool _disposed;

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
    public override void ThrowIfClosed()
    {
        // _closed is set after a successful Commit or Rollback; _committed implies _closed
        // (kept separately so Dispose can distinguish "released" from "rolled back at exit").
        // _disposed is set by DisposeAsync. Any of these terminal states blocks further forwarding.
        if (_closed || _committed || _disposed)
        {
            throw new ObjectDisposedException(
                nameof(SavepointInquiryTransaction),
                "This Inquiry savepoint has already been committed, rolled back, or disposed. " +
                "Calls routed through the savepoint handle (tx.X) after close are not allowed.");
        }
    }

    /// <inheritdoc />
    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SavepointInquiryTransaction));
        if (_closed) return;

        try
        {
            try
            {
                await _outerPipeline.ReleaseSavepointAsync(_savepointName, cancellationToken).ConfigureAwait(false);
                _committed = true;
            }
            catch (NotSupportedException)
            {
                // Oracle: savepoints cannot be explicitly released. They are released implicitly
                // when the outer transaction commits / rolls back. Treat as committed locally so
                // Dispose doesn't attempt a rollback.
                _committed = true;
            }
        }
        finally
        {
            // Even if ReleaseSavepointAsync threw something other than NotSupportedException
            // (e.g. the outer transaction was rolled back externally, or the savepoint name is
            // gone), the handle is finished. Marking _closed fails-fast subsequent forwarding
            // calls and stops DisposeAsync from attempting another rollback-to-savepoint.
            _closed = true;
        }
    }

    /// <inheritdoc />
    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SavepointInquiryTransaction));
        if (_closed) return;

        try
        {
            await _outerPipeline.RollbackToSavepointAsync(_savepointName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Same finally-Close pattern as CommitAsync: even on failure the handle is done.
            _closed = true;
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_closed || _committed) return;

        try
        {
            await _outerPipeline.RollbackToSavepointAsync(_savepointName, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: the outer transaction may already be closed, the savepoint may
            // have been auto-released, or another operation may be in flight. Swallow so
            // we don't mask a real exception from the user's using-block.
        }
    }
}
