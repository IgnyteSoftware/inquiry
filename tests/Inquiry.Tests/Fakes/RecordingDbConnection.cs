using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Inquiry.Tests.Fakes;

internal sealed class RecordingDbConnection : DbConnection
{
    private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _resultSets = new();
    private ConnectionState _state = ConnectionState.Closed;

    public List<RecordingDbCommand> Commands { get; } = new();

    public Dictionary<string, object?> OutputParameterValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int RowsAffected { get; set; } = 1;

    public override string ConnectionString { get; set; } = "Data Source=:memory:";

    public override string Database => "inquiry";

    public override string DataSource => "recording";

    public override string ServerVersion => "1";

    public override ConnectionState State => _state;

    public void QueueResultSet(params IReadOnlyDictionary<string, object?>[] rows)
    {
        _resultSets.Enqueue(rows);
    }

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    public override Task CloseAsync()
    {
        Close();
        return Task.CompletedTask;
    }

    public override void Open()
    {
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        Open();
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        return new RecordingDbTransaction(this, isolationLevel);
    }

    protected override DbCommand CreateDbCommand()
    {
        var command = new RecordingDbCommand(this);
        Commands.Add(command);
        return command;
    }

    internal IReadOnlyList<IReadOnlyDictionary<string, object?>> DequeueResultSet()
    {
        return _resultSets.Count == 0
            ? Array.Empty<IReadOnlyDictionary<string, object?>>()
            : _resultSets.Dequeue();
    }
}

internal sealed class RecordingDbTransaction : DbTransaction
{
    public RecordingDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
    {
        DbConnection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection DbConnection { get; }

    public bool Committed { get; private set; }

    public bool RolledBack { get; private set; }

    public override void Commit()
    {
        Committed = true;
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();
        return Task.CompletedTask;
    }

    public override void Rollback()
    {
        RolledBack = true;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
        return Task.CompletedTask;
    }
}

internal sealed class RecordingDbCommand : DbCommand
{
    private readonly RecordingDbConnection _connection;
    private readonly RecordingDbParameterCollection _parameters = new();

    public RecordingDbCommand(RecordingDbConnection connection)
    {
        _connection = connection;
        DbConnection = connection;
    }

    public IReadOnlyList<DbParameter> RecordedParameters => _parameters.Items;

    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public DbTransaction? RecordedTransaction => DbTransaction;

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
    {
        foreach (DbParameter parameter in _parameters)
        {
            if (parameter.Direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue &&
                _connection.OutputParameterValues.TryGetValue(parameter.ParameterName, out var value))
            {
                parameter.Value = value ?? DBNull.Value;
            }
        }

        return _connection.RowsAffected;
    }

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ExecuteNonQuery());
    }

    public override object? ExecuteScalar()
    {
        return null;
    }

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ExecuteScalar());
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
    {
        return new RecordingDbParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return new RecordingDbDataReader(_connection.DequeueResultSet());
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        return Task.FromResult<DbDataReader>(new RecordingDbDataReader(_connection.DequeueResultSet()));
    }
}

internal sealed class RecordingDbParameter : DbParameter
{
    public override DbType DbType { get; set; }

    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public override bool IsNullable { get; set; }

    public override string ParameterName { get; set; } = string.Empty;

    public override string SourceColumn { get; set; } = string.Empty;

    public override object? Value { get; set; }

    public override bool SourceColumnNullMapping { get; set; }

    public override int Size { get; set; }

    public override void ResetDbType()
    {
    }
}

internal sealed class RecordingDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();

    public IReadOnlyList<DbParameter> Items => _items;

    public override int Count => _items.Count;

    public override object SyncRoot => ((ICollection)_items).SyncRoot;

    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    public override void Clear()
    {
        _items.Clear();
    }

    public override bool Contains(object value)
    {
        return _items.Contains((DbParameter)value);
    }

    public override bool Contains(string value)
    {
        return _items.Any(parameter => parameter.ParameterName == value);
    }

    public override void CopyTo(Array array, int index)
    {
        ((ICollection)_items).CopyTo(array, index);
    }

    public override IEnumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    public override int IndexOf(object value)
    {
        return _items.IndexOf((DbParameter)value);
    }

    public override int IndexOf(string parameterName)
    {
        return _items.FindIndex(parameter => parameter.ParameterName == parameterName);
    }

    public override void Insert(int index, object value)
    {
        _items.Insert(index, (DbParameter)value);
    }

    public override void Remove(object value)
    {
        _items.Remove((DbParameter)value);
    }

    public override void RemoveAt(int index)
    {
        _items.RemoveAt(index);
    }

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    protected override DbParameter GetParameter(int index)
    {
        return _items[index];
    }

    protected override DbParameter GetParameter(string parameterName)
    {
        return _items[IndexOf(parameterName)];
    }

    protected override void SetParameter(int index, DbParameter value)
    {
        _items[index] = value;
    }

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            Add(value);
        }
        else
        {
            _items[index] = value;
        }
    }
}

internal sealed class RecordingDbDataReader : DbDataReader
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;
    private readonly string[] _columns;
    private int _position = -1;

    public RecordingDbDataReader(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        _rows = rows;
        _columns = rows.Count == 0 ? Array.Empty<string>() : rows[0].Keys.ToArray();
    }

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override int Depth => 0;

    public override int FieldCount => _columns.Length;

    public override bool HasRows => _rows.Count > 0;

    public override bool IsClosed { get; } = false;

    public override int RecordsAffected => _rows.Count;

    public override bool GetBoolean(int ordinal)
    {
        return Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override byte GetByte(int ordinal)
    {
        return Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var bytes = (byte[])GetValue(ordinal);
        if (buffer is null)
        {
            return bytes.Length;
        }

        var available = Math.Min(length, bytes.Length - (int)dataOffset);
        Array.Copy(bytes, dataOffset, buffer, bufferOffset, available);
        return available;
    }

    public override char GetChar(int ordinal)
    {
        return Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var chars = Convert.ToString(GetValue(ordinal), CultureInfo.InvariantCulture)!.ToCharArray();
        if (buffer is null)
        {
            return chars.Length;
        }

        var available = Math.Min(length, chars.Length - (int)dataOffset);
        Array.Copy(chars, dataOffset, buffer, bufferOffset, available);
        return available;
    }

    public override string GetDataTypeName(int ordinal)
    {
        return GetFieldType(ordinal).Name;
    }

    public override DateTime GetDateTime(int ordinal)
    {
        return Convert.ToDateTime(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override decimal GetDecimal(int ordinal)
    {
        return Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override double GetDouble(int ordinal)
    {
        return Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override IEnumerator GetEnumerator()
    {
        while (Read())
        {
            yield return this;
        }
    }

    public override Type GetFieldType(int ordinal)
    {
        var value = GetValue(ordinal);
        return value == DBNull.Value || value is null ? typeof(object) : value.GetType();
    }

    public override float GetFloat(int ordinal)
    {
        return Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    public override short GetInt16(int ordinal)
    {
        return Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override int GetInt32(int ordinal)
    {
        return Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override long GetInt64(int ordinal)
    {
        return Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public override string GetName(int ordinal)
    {
        return _columns[ordinal];
    }

    public override int GetOrdinal(string name)
    {
        var ordinal = Array.FindIndex(_columns, column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));
        return ordinal >= 0 ? ordinal : throw new IndexOutOfRangeException(name);
    }

    public override string GetString(int ordinal)
    {
        return Convert.ToString(GetValue(ordinal), CultureInfo.InvariantCulture)!;
    }

    public override object GetValue(int ordinal)
    {
        var value = _rows[_position][_columns[ordinal]];
        return value ?? DBNull.Value;
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, _columns.Length);
        for (var index = 0; index < count; index++)
        {
            values[index] = GetValue(index);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is null or DBNull;
    }

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        return Task.FromResult(IsDBNull(ordinal));
    }

    public override bool NextResult()
    {
        return false;
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public override bool Read()
    {
        if (_position + 1 >= _rows.Count)
        {
            return false;
        }

        _position++;
        return true;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Read());
    }

    public override DataTable? GetSchemaTable()
    {
        return null;
    }
}
