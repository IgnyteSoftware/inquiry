using System.Data;

namespace Inquiry.Testing;

/// <summary>
/// A transaction or savepoint begun on a <see cref="ScriptedDatabase"/> connection, and how it
/// ended. A nested Inquiry transaction runs as a savepoint, so it is recorded as a scope whose
/// <see cref="Parent"/> is the scope it was begun in.
/// </summary>
public sealed class RecordedTransaction
{
    private int _outcome;

    internal RecordedTransaction(IsolationLevel isolationLevel, RecordedTransaction? parent = null)
    {
        IsolationLevel = isolationLevel;
        Parent = parent;
    }

    /// <summary>Gets the isolation level the runtime requested. A savepoint has its parent's level.</summary>
    public IsolationLevel IsolationLevel { get; }

    /// <summary>
    /// Gets the scope a savepoint was begun in, or <see langword="null"/> for a transaction.
    /// </summary>
    public RecordedTransaction? Parent { get; }

    /// <summary>
    /// Gets how the work in this scope ended. A transaction disposed without a commit or rollback
    /// counts as rolled back, as it does on a real connection. A savepoint is
    /// <see cref="RecordedTransactionOutcome.RolledBack"/> when the runtime rolls back to it or to a
    /// savepoint begun before it, or when its transaction rolls back. A released savepoint stays
    /// <see cref="RecordedTransactionOutcome.Active"/> until its transaction ends, and then takes
    /// the transaction's outcome.
    /// </summary>
    public RecordedTransactionOutcome Outcome
    {
        get => (RecordedTransactionOutcome)Volatile.Read(ref _outcome);
        internal set => Volatile.Write(ref _outcome, (int)value);
    }
}

/// <summary>How a <see cref="RecordedTransaction"/> ended.</summary>
public enum RecordedTransactionOutcome
{
    /// <summary>The transaction has not committed or rolled back yet.</summary>
    Active,

    /// <summary>The transaction committed.</summary>
    Committed,

    /// <summary>The transaction rolled back, or was disposed without a commit.</summary>
    RolledBack,
}
