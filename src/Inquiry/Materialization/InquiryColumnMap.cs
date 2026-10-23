using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.Materialization;

/// <summary>
/// The result-column ordinal of each property of an <see cref="InquiryResultShape"/> for one result set.
/// </summary>
/// <remarks>
/// A map is immutable and can be shared by every reader whose result set has the same column names.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryColumnMap
{
    private readonly InquiryResultShape _shape;
    private readonly string[]? _resultColumns;

    internal InquiryColumnMap(InquiryResultShape shape, string[]? resultColumns, int[] ordinals, int[] readOrder, bool isInOrder)
    {
        _shape = shape;
        _resultColumns = resultColumns;
        Ordinals = ordinals;
        ReadOrder = readOrder;
        IsInOrder = isInOrder;
    }

    /// <summary>
    /// Gets whether property <c>i</c> reads column <c>i</c> for every property, so the result can be read
    /// in declaration order.
    /// </summary>
    public bool IsInOrder { get; }

    /// <summary>
    /// Gets the result-column ordinal of each property, indexed by property declaration order.
    /// </summary>
    public int[] Ordinals { get; }

    /// <summary>
    /// Gets the property indexes in ascending column-ordinal order. Reading in this order keeps
    /// <see cref="System.Data.CommandBehavior.SequentialAccess"/> correct.
    /// </summary>
    public int[] ReadOrder { get; }

    /// <summary>
    /// Returns whether a materializer should wrap <paramref name="exception"/> with the property and
    /// column that it was reading. Provider, I/O and cancellation failures pass through unchanged.
    /// </summary>
    public static bool IsReadFailure(Exception exception)
        => exception is not (DbException or OperationCanceledException or IOException or TimeoutException or OutOfMemoryException);

    /// <summary>
    /// Creates the exception thrown when the value for <paramref name="property"/> cannot be read.
    /// </summary>
    public InvalidOperationException ReadFailed(DbDataReader reader, int property, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(exception);
        var ordinal = Ordinals[property];
        var column = ColumnName(ordinal);
        var target = $"'{_shape.ResultType}.{_shape.PropertyNames[property]}'";
        if (IsNull(reader, ordinal))
        {
            return new InvalidOperationException(
                $"Inquiry cannot read column '{column}' (ordinal {ordinal}) into property {target}: the value is NULL and the property type does not allow null. " +
                "Make the property nullable, or exclude NULL values in the SQL.",
                exception);
        }

        return new InvalidOperationException(
            $"Inquiry cannot read column '{column}' (ordinal {ordinal}) into property {target}. Check that the column type is compatible with the property type. {exception.Message}",
            exception);
    }

    internal bool HasColumns(DbDataReader reader, int fieldCount)
    {
        var resultColumns = _resultColumns!;
        if (resultColumns.Length != fieldCount)
        {
            return false;
        }

        for (var ordinal = 0; ordinal < fieldCount; ordinal++)
        {
            if (!string.Equals(reader.GetName(ordinal), resultColumns[ordinal], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // The in-order map has no result names, but its columns matched the shape's names in order.
    private string ColumnName(int ordinal) => _resultColumns?[ordinal] ?? _shape.ColumnNames[ordinal];

    // The provider's own exception for a NULL differs by provider, so ask the reader. Some providers
    // refuse the probe after a failed read under SequentialAccess; the generic message then applies.
    private static bool IsNull(DbDataReader reader, int ordinal)
    {
        try
        {
            return reader.IsDBNull(ordinal);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return false;
        }
    }
}
