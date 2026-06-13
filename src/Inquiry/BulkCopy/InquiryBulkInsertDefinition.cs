using System.Collections.Generic;
using System.ComponentModel;

namespace Inquiry.BulkCopy;

/// <summary>
/// The compile-time shape of a bulk insert for one entity: target table, insertable column names
/// (in bind order), and an ordinal value accessor. Emitted by the source generator as a static
/// field on the store; consumed by the provider's <see cref="IInquiryBulkCopier"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type being bulk-inserted.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InquiryBulkInsertDefinition<TEntity>
    where TEntity : class
{
    /// <summary>Initializes the definition.</summary>
    /// <param name="schema">Raw schema name, or null for the default schema.</param>
    /// <param name="table">Raw (unquoted) table name; the copier quotes per its dialect.</param>
    /// <param name="columns">Raw (unquoted) column names in accessor-ordinal order.</param>
    /// <param name="getValue">
    /// Returns the provider-primitive value for (entity, columnOrdinal) — converters and enum
    /// coercions already applied by the generator; <see cref="DBNull.Value"/> for null.
    /// </param>
    public InquiryBulkInsertDefinition(string? schema, string table, string[] columns, Func<TEntity, int, object> getValue)
    {
        Schema = schema;
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        GetValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        if (columns.Length == 0) throw new ArgumentException("A bulk insert needs at least one column.", nameof(columns));
    }

    /// <summary>Raw schema name, or null for the provider default.</summary>
    public string? Schema { get; }

    /// <summary>Raw (unquoted) table name.</summary>
    public string Table { get; }

    /// <summary>Raw (unquoted) column names, in the accessor's ordinal order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Ordinal value accessor: (entity, columnOrdinal) → provider value or <see cref="DBNull.Value"/>.</summary>
    public Func<TEntity, int, object> GetValue { get; }
}
