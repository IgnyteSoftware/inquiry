using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Inquiry.Testing;

internal sealed class ScriptedCommand : DbCommand
{
    private readonly ScriptedParameterCollection _parameters;

    public ScriptedCommand(ScriptedConnection connection)
    {
        DbConnection = connection;
        _parameters = new ScriptedParameterCollection(connection.ProviderFactory);
    }

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; } = 30;

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override UpdateRowSource UpdatedRowSource { get; set; }

    public override bool DesignTimeVisible { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
        => Connection.ProviderFactory.CreateParameter()
            ?? throw new InvalidOperationException("The provider factory did not create a parameter.");

    public override int ExecuteNonQuery()
    {
        var (result, recorded) = Run();
        return ScriptedExecution.NonQuery(result, recorded);
    }

    public override object? ExecuteScalar()
    {
        var (result, _) = Run();
        return ScriptedExecution.Scalar(result);
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var (result, recorded) = Run();
        ScriptedExecution.RecordReader(result, recorded);
        return new ScriptedDataReader(
            result.ResultSets, result.RecordsAffected, behavior,
            (behavior & CommandBehavior.CloseConnection) != 0 ? Connection : null,
            ScriptedProviderRules.For(Connection.ProviderFactory));
    }

    private new ScriptedConnection Connection
        => DbConnection as ScriptedConnection
            ?? throw new InvalidOperationException("A scripted command needs a scripted connection.");

    private (ScriptedResult Result, RecordedCommand Command) Run()
        => Connection.Execute(CommandText, CommandType, _parameters, DbTransaction);
}

internal static class ScriptedExecution
{
    public static int NonQuery(ScriptedResult result, RecordedCommand recorded)
    {
        if (result.ResultSets.Length == 0)
        {
            recorded.RecordsAffected = result.RecordsAffected;
            return result.RecordsAffected;
        }

        return -1;
    }

    public static object? Scalar(ScriptedResult result)
        => result.ResultSets.Length > 0 && result.ResultSets[0].Rows.Length > 0
            ? result.ResultSets[0].Rows[0][0]
            : null;

    public static void RecordReader(ScriptedResult result, RecordedCommand recorded)
    {
        if (result.ResultSets.Length == 0)
        {
            recorded.RecordsAffected = result.RecordsAffected;
        }
    }
}

internal sealed class ScriptedParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();

    // The provider's own collection accepts only its parameter type, so a foreign parameter fails here too.
    private readonly Type _parameterType;

    // SqlClient lets a parameter belong to one collection at a time. The owner lookup applies that
    // rule across every scripted command.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbParameter, ScriptedParameterCollection> _owners = new();
    private readonly bool _oneCollection;

    public ScriptedParameterCollection(DbProviderFactory factory)
    {
        _parameterType = factory.CreateParameter()?.GetType() ?? typeof(DbParameter);
        _oneCollection = ScriptedProviderRules.For(factory).ParameterHasOneCollection;
    }

    public override int Count => _items.Count;

    public override object SyncRoot => ((ICollection)_items).SyncRoot;

    public override int Add(object value)
    {
        _items.Add(Claim(Cast(value)));
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            Add(value);
        }
    }

    public override void Clear() => _items.Clear();

    public override bool Contains(object value) => value is DbParameter parameter && _items.Contains(parameter);

    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(object value) => value is DbParameter parameter ? _items.IndexOf(parameter) : -1;

    public override int IndexOf(string parameterName)
    {
        var index = _items.FindIndex(item => string.Equals(item.ParameterName, parameterName, StringComparison.Ordinal));
        return index >= 0
            ? index
            : _items.FindIndex(item => string.Equals(item.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    public override void Insert(int index, object value) => _items.Insert(index, Claim(Cast(value)));

    public override void Remove(object value) => _items.Remove(Cast(value));

    public override void RemoveAt(int index) => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOfRequired(parameterName));

    protected override DbParameter GetParameter(int index) => _items[index];

    protected override DbParameter GetParameter(string parameterName) => _items[IndexOfRequired(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => Replace(index, value);

    protected override void SetParameter(string parameterName, DbParameter value) => Replace(IndexOfRequired(parameterName), value);

    private void Replace(int index, DbParameter value)
    {
        var parameter = Cast(value);
        if (!ReferenceEquals(_items[index], parameter))
        {
            _items[index] = Claim(parameter);
        }
    }

    private DbParameter Claim(DbParameter parameter)
    {
        if (_oneCollection)
        {
            if (_owners.TryGetValue(parameter, out var owner) && owner._items.Contains(parameter))
            {
                throw new ArgumentException(
                    $"The {_parameterType.Name} is already contained by another {_parameterType.Name}Collection.", nameof(parameter));
            }

            _owners.AddOrUpdate(parameter, this);
        }

        return parameter;
    }

    private int IndexOfRequired(string parameterName)
    {
        var index = IndexOf(parameterName);
        // Matches the provider collections, which throw IndexOutOfRangeException for an unknown name.
#pragma warning disable CA2201, S112
        return index >= 0 ? index : throw new IndexOutOfRangeException($"The command has no parameter named '{parameterName}'.");
#pragma warning restore CA2201, S112
    }

    private DbParameter Cast(object value)
        => value is DbParameter parameter && _parameterType.IsInstanceOfType(parameter)
            ? parameter
            : throw new InvalidCastException(
                $"The provider's parameter collection accepts only {_parameterType.Name} values, not {value?.GetType().Name ?? "null"}.");
}

internal sealed class ScriptedBatch : DbBatch
{
    private readonly ScriptedBatchCommandCollection _commands = new();
    public ScriptedBatch(ScriptedConnection connection) => DbConnection = connection;

    public override int Timeout { get; set; }

    protected override DbBatchCommandCollection DbBatchCommands => _commands;

    protected override DbConnection? DbConnection { get; set; }

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override void Prepare()
    {
    }

    public override Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    // Like SqlClient, the total stays -1 until some command reports a count.
    public override int ExecuteNonQuery()
    {
        var total = -1;
        foreach (var (command, result, recorded) in Run())
        {
            var affected = ScriptedExecution.NonQuery(result, recorded);
            command.SetRecordsAffected(affected);
            if (affected >= 0)
            {
                total = Math.Max(total, 0) + affected;
            }
        }

        return total;
    }

    public override object? ExecuteScalar()
    {
        var results = Run().ToList();
        return results.Count == 0 ? null : ScriptedExecution.Scalar(results[0].Result);
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteNonQuery());
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteScalar());
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var resultSets = new List<ScriptedResultSetData>();
        var total = -1;
        foreach (var (command, result, recorded) in Run())
        {
            ScriptedExecution.RecordReader(result, recorded);
            resultSets.AddRange(result.ResultSets);
            if (result.ResultSets.Length == 0)
            {
                command.SetRecordsAffected(result.RecordsAffected);
                if (result.RecordsAffected >= 0)
                {
                    total = Math.Max(total, 0) + result.RecordsAffected;
                }
            }
        }

        return new ScriptedDataReader(
            resultSets.ToArray(), total, behavior,
            (behavior & CommandBehavior.CloseConnection) != 0 ? Connection : null,
            ScriptedProviderRules.For(Connection.ProviderFactory));
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ExecuteDbDataReader(behavior));
    }

    protected override DbBatchCommand CreateDbBatchCommand() => new ScriptedBatchCommand(Connection.ProviderFactory);

    private new ScriptedConnection Connection
        => DbConnection as ScriptedConnection
            ?? throw new InvalidOperationException("A scripted batch needs a scripted connection.");

    // Lazy, so each caller records an item before the next one runs. A later failure then keeps
    // the outcomes of the commands before it.
    private IEnumerable<(ScriptedBatchCommand Command, ScriptedResult Result, RecordedCommand Recorded)> Run()
    {
        var connection = Connection;
        foreach (var command in _commands.Items)
        {
            var (result, recorded) = connection.Execute(command.CommandText, command.CommandType, command.Parameters, DbTransaction);
            yield return (command, result, recorded);
        }
    }
}

internal sealed class ScriptedBatchCommand : DbBatchCommand
{
    private readonly DbProviderFactory _factory;
    private readonly ScriptedParameterCollection _parameters;
    private int _recordsAffected = -1;

    public ScriptedBatchCommand(DbProviderFactory factory)
    {
        _factory = factory;
        _parameters = new ScriptedParameterCollection(factory);
    }

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override int RecordsAffected => _recordsAffected;

    public override bool CanCreateParameter => true;

    protected override DbParameterCollection DbParameterCollection => _parameters;

    public override DbParameter CreateParameter()
        => _factory.CreateParameter() ?? throw new InvalidOperationException("The provider factory did not create a parameter.");

    internal void SetRecordsAffected(int recordsAffected) => _recordsAffected = recordsAffected;
}

internal sealed class ScriptedBatchCommandCollection : DbBatchCommandCollection
{
    private readonly List<ScriptedBatchCommand> _items = new();

    internal IReadOnlyList<ScriptedBatchCommand> Items => _items;

    public override int Count => _items.Count;

    public override bool IsReadOnly => false;

    public override void Add(DbBatchCommand item) => _items.Add(Cast(item));

    public override void Clear() => _items.Clear();

    public override bool Contains(DbBatchCommand item) => item is ScriptedBatchCommand command && _items.Contains(command);

    public override void CopyTo(DbBatchCommand[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var i = 0; i < _items.Count; i++)
        {
            array[arrayIndex + i] = _items[i];
        }
    }

    public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(DbBatchCommand item) => item is ScriptedBatchCommand command ? _items.IndexOf(command) : -1;

    public override void Insert(int index, DbBatchCommand item) => _items.Insert(index, Cast(item));

    public override bool Remove(DbBatchCommand item) => item is ScriptedBatchCommand command && _items.Remove(command);

    public override void RemoveAt(int index) => _items.RemoveAt(index);

    protected override DbBatchCommand GetBatchCommand(int index) => _items[index];

    protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => _items[index] = Cast(batchCommand);

    private static ScriptedBatchCommand Cast(DbBatchCommand item)
        => item as ScriptedBatchCommand ?? throw new InvalidCastException("A scripted batch accepts only its own batch commands.");
}
