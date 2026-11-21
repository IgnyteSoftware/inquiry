namespace Inquiry.Testing;

/// <summary>
/// One scripted result set: its columns, in order, and its rows. Pass it to
/// <see cref="ScriptedResult.Rows(ScriptedResultSet[])"/>.
/// </summary>
/// <remarks>
/// The scripted reader does not convert values the way a provider does. Give each value the CLR
/// type of its column, which is the type the mapped member reads.
/// <code>
/// var customers = new ScriptedResultSet()
///     .Column&lt;int&gt;("Id")
///     .Column&lt;string&gt;("Name")
///     .Row(1, "Ada")
///     .Row(2, null);
/// </code>
/// </remarks>
public sealed class ScriptedResultSet
{
    private readonly List<string> _names = new();
    private readonly List<Type> _types = new();
    private readonly List<object?[]> _rows = new();

    /// <summary>
    /// Adds a column. A nullable value type, such as <c>int?</c>, reports its underlying type, as a
    /// provider does. Add every column before the first row.
    /// </summary>
    /// <typeparam name="T">The CLR type of the column's values.</typeparam>
    /// <param name="name">The column name the reader reports.</param>
    /// <returns>This result set.</returns>
    public ScriptedResultSet Column<T>(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_rows.Count > 0)
        {
            throw new InvalidOperationException("Add every column before the first row.");
        }

        _names.Add(name);
        _types.Add(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        return this;
    }

    /// <summary>
    /// Adds a row. Give one value per column, in column order. Use <see langword="null"/> or
    /// <see cref="DBNull.Value"/> for a database null.
    /// </summary>
    /// <param name="values">The row's values.</param>
    /// <returns>This result set.</returns>
    public ScriptedResultSet Row(params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != _names.Count)
        {
            throw new ArgumentException(
                $"The row has {values.Length} values, but the result set has {_names.Count} columns.", nameof(values));
        }

        var row = new object?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i] ?? DBNull.Value;
            if (value is not DBNull && !_types[i].IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"Column '{_names[i]}' holds {_types[i].Name} values, but the row gives {value.GetType().Name}.", nameof(values));
            }

            row[i] = value;
        }

        _rows.Add(row);
        return this;
    }

    internal ScriptedResultSetData Snapshot()
    {
        if (_names.Count == 0)
        {
            throw new InvalidOperationException("A scripted result set needs at least one column.");
        }

        return new ScriptedResultSetData(_names.ToArray(), _types.ToArray(), _rows.ToArray());
    }
}

internal sealed record ScriptedResultSetData(string[] Names, Type[] Types, object?[][] Rows);
