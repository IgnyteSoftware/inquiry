using System.ComponentModel;
using System.Data.Common;
using System.Text;

namespace Inquiry.Materialization;

/// <summary>
/// Describes the properties of a generated result type and maps result columns to them by name.
/// </summary>
/// <remarks>
/// Generated materializers for handwritten SQL keep one shape per result type. Names compare with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>. Extra result columns are ignored. A missing
/// column, or a property that more than one column matches, throws before any row is read.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryResultShape
{
    private readonly Dictionary<string, int> _propertyByColumn;
    private readonly InquiryColumnMap _inOrder;
    private InquiryColumnMap? _last;

    /// <summary>
    /// Creates a shape. <paramref name="propertyNames"/> and <paramref name="columnNames"/> are parallel
    /// arrays in property declaration order, and the column names are unique under the name comparer.
    /// </summary>
    public InquiryResultShape(Type resultType, string[] propertyNames, string[] columnNames)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(propertyNames);
        ArgumentNullException.ThrowIfNull(columnNames);
        if (propertyNames.Length != columnNames.Length)
        {
            throw new ArgumentException("Each property needs exactly one column name.", nameof(columnNames));
        }

        ResultType = resultType;
        PropertyNames = propertyNames;
        ColumnNames = columnNames;
        _propertyByColumn = new Dictionary<string, int>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
        for (var property = 0; property < columnNames.Length; property++)
        {
            if (!_propertyByColumn.TryAdd(columnNames[property], property))
            {
                throw new ArgumentException(
                    $"Properties '{propertyNames[_propertyByColumn[columnNames[property]]]}' and '{propertyNames[property]}' of '{resultType}' both map to column '{columnNames[property]}'.",
                    nameof(columnNames));
            }
        }

        var identity = new int[columnNames.Length];
        for (var property = 0; property < identity.Length; property++)
        {
            identity[property] = property;
        }

        _inOrder = new InquiryColumnMap(this, resultColumns: null, identity, identity, isInOrder: true);
    }

    internal Type ResultType { get; }

    internal string[] PropertyNames { get; }

    internal string[] ColumnNames { get; }

    /// <summary>
    /// Maps the columns of the reader's current result set to the properties of this shape.
    /// </summary>
    /// <exception cref="InvalidOperationException">A property has no matching column, or more than one.</exception>
    public InquiryColumnMap Map(DbDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var fieldCount = reader.FieldCount;
        if (fieldCount == ColumnNames.Length && IsInOrder(reader))
        {
            return _inOrder;
        }

        // A reordered or wider result usually repeats for the same SQL. Reusing the last map when every
        // column name is identical avoids a new map per read without trusting the SQL text. Only the
        // most recent shape is kept, so two shapes that alternate resolve on each read.
        var last = Volatile.Read(ref _last);
        if (last is not null && last.HasColumns(reader, fieldCount))
        {
            return last;
        }

        var map = Resolve(reader, fieldCount);
        Volatile.Write(ref _last, map);
        return map;
    }

    private bool IsInOrder(DbDataReader reader)
    {
        for (var ordinal = 0; ordinal < ColumnNames.Length; ordinal++)
        {
            if (!string.Equals(reader.GetName(ordinal), ColumnNames[ordinal], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private InquiryColumnMap Resolve(DbDataReader reader, int fieldCount)
    {
        // Read every name first, so an error can list all of the available columns.
        var resultColumns = new string[fieldCount];
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            resultColumns[ordinal] = reader.GetName(ordinal);
        }

        var ordinals = new int[ColumnNames.Length];
        Array.Fill(ordinals, -1);
        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            if (!_propertyByColumn.TryGetValue(resultColumns[ordinal], out var property))
            {
                continue;
            }

            if (ordinals[property] >= 0)
            {
                throw new InvalidOperationException(
                    $"Inquiry cannot map the result to '{ResultType}': columns {ordinals[property]} ('{resultColumns[ordinals[property]]}') and {ordinal} ('{resultColumns[ordinal]}') " +
                    $"both match property '{PropertyNames[property]}'. Give each result column a unique name. Available columns: {FormatColumns(resultColumns)}.");
            }

            ordinals[property] = ordinal;
        }

        var isInOrder = true;
        for (var property = 0; property < ordinals.Length; property++)
        {
            if (ordinals[property] < 0)
            {
                throw new InvalidOperationException(
                    $"Inquiry cannot map the result to '{ResultType}': property '{PropertyNames[property]}' needs a column named '{ColumnNames[property]}', " +
                    $"but the result has none. Available columns: {FormatColumns(resultColumns)}.");
            }

            isInOrder &= ordinals[property] == property;
        }

        // Extra columns after the mapped ones leave the properties in order, so rows can still be
        // read straight through.
        var readOrder = new int[ordinals.Length];
        for (var property = 0; property < readOrder.Length; property++)
        {
            readOrder[property] = property;
        }

        if (!isInOrder)
        {
            Array.Sort(ordinals.ToArray(), readOrder);
        }

        return new InquiryColumnMap(this, resultColumns, ordinals, readOrder, isInOrder);
    }

    private static string FormatColumns(string[] resultColumns)
    {
        if (resultColumns.Length == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        for (var ordinal = 0; ordinal < resultColumns.Length; ordinal++)
        {
            if (ordinal > 0)
            {
                builder.Append(", ");
            }

            builder.Append(resultColumns[ordinal].Length == 0 ? "(unnamed)" : resultColumns[ordinal]);
        }

        return builder.ToString();
    }
}
