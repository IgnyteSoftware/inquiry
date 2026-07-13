using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.BulkCopy;

/// <summary>
/// Minimal forward-only <see cref="DbDataReader"/> over an entity stream + ordinal accessor, for
/// bulk-copy APIs that consume <c>IDataReader</c> (SqlBulkCopy, MySqlBulkCopy). Supports exactly
/// what those writers call — <see cref="Read"/>, <see cref="GetValue"/>, <see cref="IsDBNull"/>,
/// <see cref="FieldCount"/>, name/ordinal lookup — and throws for everything else.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryBulkRowReader<TEntity> : DbDataReader
    where TEntity : class
{
    private readonly InquiryBulkInsertDefinition<TEntity> _definition;
    private readonly IEnumerator<TEntity> _rows;
    private TEntity? _current;
    private TEntity? _lookahead;
    private bool _hasLookahead;
    private bool _hasRowsKnown;
    private bool _hasRows;
    private bool _isClosed;
    private long _rowsRead;

    /// <summary>Initializes the reader over <paramref name="rows"/>.</summary>
    public InquiryBulkRowReader(InquiryBulkInsertDefinition<TEntity> definition, IEnumerable<TEntity> rows)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _rows = (rows ?? throw new ArgumentNullException(nameof(rows))).GetEnumerator();
    }

    /// <summary>Rows consumed so far — the rows-written count once the copy completes.</summary>
    public long RowsRead => _rowsRead;

    /// <inheritdoc />
    public override int FieldCount => _definition.Columns.Count;

    /// <inheritdoc />
    public override bool Read()
    {
        ThrowIfClosed();

        if (_hasLookahead)
        {
            _current = _lookahead;
            _lookahead = null;
            _hasLookahead = false;
            _rowsRead++;
            return true;
        }

        if (!_rows.MoveNext())
        {
            _current = null;
            _hasRowsKnown = true;
            return false;
        }

        _current = _rows.Current;
        _hasRows = true;
        _hasRowsKnown = true;
        _rowsRead++;
        return true;
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return _definition.GetValue(Current, ordinal);
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
        => GetValue(ordinal) is DBNull;

    /// <inheritdoc />
    public override string GetName(int ordinal) => _definition.Columns[ordinal];

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < _definition.Columns.Count; i++)
        {
            if (string.Equals(_definition.Columns[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new IndexOutOfRangeException($"Column '{name}' is not part of this bulk insert.");
    }

    private TEntity Current => _current ?? throw new InvalidOperationException("Read() has not advanced to a row.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isClosed)
        {
            _rows.Dispose();
            _current = null;
            _lookahead = null;
            _hasLookahead = false;
            _isClosed = true;
        }

        base.Dispose(disposing);
    }

    // ---- Surface bulk-copy writers don't call ------------------------------------------------

    /// <inheritdoc />
    public override bool HasRows
    {
        get
        {
            ThrowIfClosed();
            if (_hasRowsKnown)
            {
                return _hasRows;
            }

            _hasRowsKnown = true;
            if (!_rows.MoveNext())
            {
                return false;
            }

            _lookahead = _rows.Current;
            _hasLookahead = true;
            _hasRows = true;
            return true;
        }
    }

    /// <inheritdoc />
    public override bool IsClosed => _isClosed;

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override int RecordsAffected => -1;

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2073",
        Justification = "The returned type is generated metadata or comes from GetType() on a live fallback "
            + "value; bulk-copy writers use it only for conversion decisions, not member reflection.")]
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
        | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        ValidateOrdinal(ordinal);
        if (_definition.FieldTypes is { } fieldTypes)
        {
            return fieldTypes[ordinal];
        }

        // Some writers probe the field type for conversion decisions; derive it from the current
        // row's value when available. DBNull yields typeof(object) — the writer falls back to the
        // destination column's type.
        var value = _current is null ? null : _definition.GetValue(_current, ordinal);
        return value is null or DBNull ? typeof(object) : value.GetType();
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        ValidateOrdinal(ordinal);
        ValidateCopyArguments(dataOffset, buffer, bufferOffset, length);
        if (GetValue(ordinal) is not byte[] value)
        {
            throw new InvalidCastException($"Column {ordinal} does not contain a byte array.");
        }

        if (buffer is null)
        {
            return value.LongLength;
        }

        if (length == 0 || dataOffset >= value.LongLength)
        {
            return 0;
        }

        var count = Math.Min(length, value.Length - (int)dataOffset);
        Array.Copy(value, (int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        ValidateOrdinal(ordinal);
        ValidateCopyArguments(dataOffset, buffer, bufferOffset, length);
        if (GetValue(ordinal) is not string value)
        {
            throw new InvalidCastException($"Column {ordinal} does not contain a string.");
        }

        if (buffer is null)
        {
            return value.Length;
        }

        if (length == 0 || dataOffset >= value.Length)
        {
            return 0;
        }

        var count = Math.Min(length, value.Length - (int)dataOffset);
        value.CopyTo((int)dataOffset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

    /// <inheritdoc />
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    private void ValidateOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)FieldCount)
        {
            throw new IndexOutOfRangeException($"Column ordinal {ordinal} is outside the bulk insert shape.");
        }
    }

    private static void ValidateCopyArguments<T>(long dataOffset, T[]? buffer, int bufferOffset, int length)
    {
        if (dataOffset < 0) throw new ArgumentOutOfRangeException(nameof(dataOffset));
        if (bufferOffset < 0) throw new ArgumentOutOfRangeException(nameof(bufferOffset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (buffer is not null && (bufferOffset > buffer.Length || length > buffer.Length - bufferOffset))
        {
            throw new ArgumentException("The destination range exceeds the supplied buffer.", nameof(buffer));
        }
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }
}
