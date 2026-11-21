using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.Testing;

internal sealed class ScriptedDataSource : DbDataSource
{
    private readonly ScriptedDatabase _database;

    public ScriptedDataSource(ScriptedDatabase database) => _database = database;

    public override string ConnectionString => "Data Source=scripted";

    protected override DbConnection CreateDbConnection() => new ScriptedConnection(_database);
}

internal sealed class ScriptedConnection : DbConnection
{
    private readonly ScriptedDatabase _database;
    private ConnectionState _state;

    public ScriptedConnection(ScriptedDatabase database) => _database = database;

    internal ScriptedTransaction? ActiveTransaction { get; set; }

    internal DbProviderFactory ProviderFactory => _database.ProviderFactory;

    private string _connectionString = "Data Source=scripted";

    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }

    public override string Database => "scripted";

    public override string DataSource => "scripted";

    public override string ServerVersion => "scripted";

    public override ConnectionState State => _state;

    public override bool CanCreateBatch => _database.ProviderFactory.CanCreateBatch;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Open()
    {
        if (_state == ConnectionState.Open)
        {
            throw new InvalidOperationException("The connection is already open.");
        }

        _state = ConnectionState.Open;
    }

    public override void Close()
    {
        // A real connection rolls back its pending transaction when it closes.
        ActiveTransaction?.Complete(RecordedTransactionOutcome.RolledBack);
        _state = ConnectionState.Closed;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        EnsureOpen();
        if (ActiveTransaction is not null)
        {
            throw new InvalidOperationException("The connection already has a pending transaction. Parallel transactions are not supported.");
        }

        ActiveTransaction = new ScriptedTransaction(this, _database, _database.BeginTransaction(isolationLevel), isolationLevel);
        return ActiveTransaction;
    }

    protected override DbCommand CreateDbCommand() => new ScriptedCommand(this);

    protected override DbBatch CreateDbBatch() => new ScriptedBatch(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Applies the checks a provider makes before it runs a command, then records the command and
    /// returns its scripted result.
    /// </summary>
    internal (ScriptedResult Result, RecordedCommand Command) Execute(
        string? commandText, CommandType commandType, DbParameterCollection parameters, DbTransaction? transaction)
    {
        EnsureOpen();
        if (string.IsNullOrEmpty(commandText))
        {
            throw new InvalidOperationException("The command text has not been set.");
        }

        if (transaction is null && ActiveTransaction is not null && ScriptedProviderRules.For(_database.ProviderFactory).CommandNeedsTransaction)
        {
            throw new InvalidOperationException(
                "The command must have a transaction when its connection has a pending local transaction.");
        }

        if (transaction is not null && !ReferenceEquals(transaction, ActiveTransaction))
        {
            throw new InvalidOperationException(
                "The command's transaction is not the connection's pending transaction, or it has completed.");
        }

        return _database.Execute(commandText, commandType, parameters, ActiveTransaction?.Innermost);
    }

    private void EnsureOpen()
    {
        if (_state != ConnectionState.Open)
        {
            throw new InvalidOperationException("The connection is not open.");
        }
    }
}

internal sealed class ScriptedTransaction : DbTransaction
{
    private readonly ScriptedDatabase _database;
    private readonly ScriptedProviderRules _rules;

    // A savepoint undoes everything begun after it, so scopes are compared by creation order. A
    // parent chain is not enough: a savepoint begun after a rollback to "a" has the transaction as
    // its parent, yet a second rollback to "a" still undoes it.
    private int _nextOrder;

    // Savepoint names that can still be rolled back to, and the scope each one began.
    private readonly List<(string Name, int Order)> _names = new();

    // Scopes that new commands run in, innermost last. A rollback or release closes a scope, but a
    // rolled-back savepoint keeps its name.
    private readonly List<(RecordedTransaction Scope, int Order)> _open = new();

    // Savepoints whose outcome is not settled yet: open ones and released ones.
    private readonly List<(RecordedTransaction Scope, int Order)> _unresolved = new();
    private ScriptedConnection? _connection;

    public ScriptedTransaction(
        ScriptedConnection connection, ScriptedDatabase database, RecordedTransaction recorded, IsolationLevel isolationLevel)
    {
        _connection = connection;
        _database = database;
        _rules = ScriptedProviderRules.For(database.ProviderFactory);
        Recorded = recorded;
        IsolationLevel = isolationLevel;
    }

    internal RecordedTransaction Recorded { get; }

    public override IsolationLevel IsolationLevel { get; }

    public override bool SupportsSavepoints => true;

    protected override DbConnection? DbConnection => _connection;

    /// <summary>Gets the innermost open scope: the latest open savepoint, or the transaction.</summary>
    internal RecordedTransaction Innermost => _open.Count > 0 ? _open[^1].Scope : Recorded;

    public override void Commit()
    {
        EnsureActive();
        Complete(RecordedTransactionOutcome.Committed);
    }

    public override void Rollback()
    {
        EnsureActive();
        Complete(RecordedTransactionOutcome.RolledBack);
    }

    // Rolls back the savepoint and every scope begun after it, including released ones. Like SQL
    // Server and SQLite, the savepoint stays nameable, but the runtime continues in the parent.
    public override void Rollback(string savepointName)
    {
        EnsureActive();
        var index = IndexOfName(savepointName);
        if (index < 0)
        {
            throw new InvalidOperationException($"No savepoint named '{savepointName}' exists.");
        }

        var order = _names[index].Order;
        _names.RemoveRange(index + 1, _names.Count - index - 1);
        _open.RemoveAll(scope => scope.Order >= order);
        _unresolved.RemoveAll(scope =>
        {
            if (scope.Order < order)
            {
                return false;
            }

            scope.Scope.Outcome = RecordedTransactionOutcome.RolledBack;
            return true;
        });
    }

    public override void Save(string savepointName)
    {
        EnsureActive();
        var scope = (_database.BeginSavepoint(Innermost), _nextOrder++);
        _names.Add((savepointName, scope.Item2));
        _open.Add(scope);
        _unresolved.Add(scope);
    }

    // A released savepoint's work joins its parent, so its outcome waits for the parent's. SqlClient
    // ignores every release; SQLite rejects an unknown name.
    public override void Release(string savepointName)
    {
        EnsureActive();
        var index = IndexOfName(savepointName);
        if (index < 0)
        {
            if (_rules.ReleaseOfUnknownSavepointFails)
            {
                throw new InvalidOperationException($"No savepoint named '{savepointName}' exists.");
            }

            return;
        }

        var order = _names[index].Order;
        _names.RemoveRange(index, _names.Count - index);
        _open.RemoveAll(scope => scope.Order >= order);
    }

    internal void Complete(RecordedTransactionOutcome outcome)
    {
        if (_connection is null)
        {
            return;
        }

        foreach (var scope in _unresolved)
        {
            scope.Scope.Outcome = outcome;
        }

        _unresolved.Clear();
        _names.Clear();
        _open.Clear();
        Recorded.Outcome = outcome;
        _connection.ActiveTransaction = null;
        _connection = null;
    }

    private int IndexOfName(string savepointName)
        => _names.FindLastIndex(savepoint => string.Equals(savepoint.Name, savepointName, _rules.SavepointNames));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Complete(RecordedTransactionOutcome.RolledBack);
        }

        base.Dispose(disposing);
    }

    private void EnsureActive()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("The transaction has completed; it is no longer usable.");
        }
    }
}
