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
        if (!_rows.MoveNext())
        {
            _current = null;
            return false;
        }

        _current = _rows.Current;
        _rowsRead++;
        return true;
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
        => _definition.GetValue(Current, ordinal);

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
        if (disposing)
        {
            _rows.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- Surface bulk-copy writers don't call ------------------------------------------------

    /// <inheritdoc />
    public override bool HasRows => true;

    /// <inheritdoc />
    public override bool IsClosed => false;

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
        Justification = "The returned type comes from GetType() on a live value the caller just produced; "
            + "bulk-copy writers use it only for conversion decisions, not member reflection, so trimmed "
            + "members are never accessed through this return value.")]
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
        | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
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
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    /// <inheritdoc />
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

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
}
