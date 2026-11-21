using System.Collections;
using System.Data;
using System.Data.Common;

namespace Inquiry.Testing;

/// <summary>
/// Reads scripted result sets. Typed getters unbox without conversion, as SqlClient does, and
/// <see cref="CommandBehavior.SequentialAccess"/> rejects a read from an earlier column where
/// the provider does; Microsoft.Data.Sqlite ignores it.
/// </summary>
internal sealed class ScriptedDataReader : DbDataReader
{
    private readonly ScriptedResultSetData[] _resultSets;
    private readonly CommandBehavior _behavior;
    private readonly bool _sequential;
    private readonly DbConnection? _closeConnection;
    private readonly int _recordsAffected;
    private int _resultIndex;
    private int _rowIndex = -1;
    private int _lastOrdinal = -1;
    private bool _closed;

    public ScriptedDataReader(
        ScriptedResultSetData[] resultSets, int recordsAffected, CommandBehavior behavior, DbConnection? closeConnection,
        ScriptedProviderRules rules)
    {
        _sequential = (behavior & CommandBehavior.SequentialAccess) != 0 && rules.EnforcesSequentialAccess;
        _resultSets = resultSets;
        _recordsAffected = recordsAffected;
        _behavior = behavior;
        _closeConnection = closeConnection;
    }

    public override int Depth => 0;

    public override int FieldCount => Current?.Names.Length ?? 0;

    public override bool HasRows => RowLimit > 0;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => _recordsAffected;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    private ScriptedResultSetData? Current
    {
        get
        {
            EnsureOpen();
            return _resultIndex < _resultSets.Length ? _resultSets[_resultIndex] : null;
        }
    }

    private int RowLimit
    {
        get
        {
            var rows = Current?.Rows.Length ?? 0;
            return (_behavior & CommandBehavior.SingleRow) != 0 ? Math.Min(rows, 1) : rows;
        }
    }

    public override bool Read()
    {
        var limit = RowLimit;
        if (_rowIndex < limit)
        {
            _rowIndex++;
        }

        _lastOrdinal = -1;
        return _rowIndex < limit;
    }

    public override bool NextResult()
    {
        EnsureOpen();
        _resultIndex = (_behavior & CommandBehavior.SingleResult) != 0 ? _resultSets.Length : _resultIndex + 1;
        _rowIndex = -1;
        _lastOrdinal = -1;
        return _resultIndex < _resultSets.Length;
    }

    public override void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _closeConnection?.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    public override string GetName(int ordinal) => Columns.Names[CheckOrdinal(ordinal)];

    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var names = Columns.Names;
        var index = Array.FindIndex(names, column => string.Equals(column, name, StringComparison.Ordinal));
        if (index < 0)
        {
            index = Array.FindIndex(names, column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));
        }

        // Matches the provider readers, which throw IndexOutOfRangeException for an unknown column.
#pragma warning disable CA2201, S112
        return index >= 0 ? index : throw new IndexOutOfRangeException(name);
#pragma warning restore CA2201, S112
    }

    public override Type GetFieldType(int ordinal) => Columns.Types[CheckOrdinal(ordinal)];

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override object GetValue(int ordinal) => Value(ordinal)!;

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => Value(ordinal) is DBNull;

    public override T GetFieldValue<T>(int ordinal)
    {
        var value = Value(ordinal);
        if (value is T typed)
        {
            return typed;
        }

        if (value is DBNull)
        {
            throw new InvalidCastException($"Column '{GetName(ordinal)}' is NULL. Call IsDBNull before reading it.");
        }

        throw new InvalidCastException(
            $"Column '{GetName(ordinal)}' holds {value!.GetType().Name}, which cannot be read as {typeof(T).Name}. "
            + "The scripted reader does not convert values; script the type the mapped member reads.");
    }

    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);

    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);

    public override char GetChar(int ordinal) => GetFieldValue<char>(ordinal);

    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);

    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);

    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);

    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);

    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);

    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);

    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);

    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);

    public override string GetString(int ordinal) => GetFieldValue<string>(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => Copy(GetFieldValue<byte[]>(ordinal), dataOffset, buffer, bufferOffset, length);

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => Copy(GetFieldValue<string>(ordinal).ToCharArray(), dataOffset, buffer, bufferOffset, length);

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, (_behavior & CommandBehavior.CloseConnection) != 0);

    private ScriptedResultSetData Columns
        => Current ?? throw new InvalidOperationException("The reader has no current result set.");

    private object? Value(int ordinal)
    {
        var columns = Columns;
        CheckOrdinal(ordinal);
        if (_rowIndex < 0 || _rowIndex >= RowLimit)
        {
            throw new InvalidOperationException("Invalid attempt to read when no data is present. Call Read first.");
        }

        if (_sequential)
        {
            if (ordinal < _lastOrdinal)
            {
                throw new InvalidOperationException(
                    $"Invalid attempt to read from column ordinal '{ordinal}'. With CommandBehavior.SequentialAccess, "
                    + $"you may only read from column ordinal '{_lastOrdinal}' or greater.");
            }

            _lastOrdinal = ordinal;
        }

        return columns.Rows[_rowIndex][ordinal];
    }

    private int CheckOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)Columns.Names.Length)
        {
            // Matches the provider readers, which throw IndexOutOfRangeException for a bad ordinal.
#pragma warning disable CA2201, S112
            throw new IndexOutOfRangeException($"Column ordinal {ordinal} is out of range.");
#pragma warning restore CA2201, S112
        }

        return ordinal;
    }

    private void EnsureOpen()
    {
        if (_closed)
        {
            throw new InvalidOperationException("Invalid attempt to use a closed reader.");
        }
    }

    private static long Copy<T>(T[] source, long dataOffset, T[]? buffer, int bufferOffset, int length)
    {
        if (buffer is null)
        {
            return source.Length;
        }

        var count = (int)Math.Max(0, Math.Min(length, source.Length - dataOffset));
        Array.Copy(source, dataOffset, buffer, bufferOffset, count);
        return count;
    }
}
