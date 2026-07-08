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
        : this(schema, table, columns, getValue, columnTypes: null)
    {
    }

    /// <summary>Initializes the definition with explicit per-column type annotations.</summary>
    /// <param name="schema"><inheritdoc cref="Schema" path="/summary"/></param>
    /// <param name="table"><inheritdoc cref="Table" path="/summary"/></param>
    /// <param name="columns"><inheritdoc cref="Columns" path="/summary"/></param>
    /// <param name="getValue"><inheritdoc cref="GetValue" path="/summary"/></param>
    /// <param name="columnTypes"><inheritdoc cref="ColumnTypes" path="/summary"/></param>
    public InquiryBulkInsertDefinition(string? schema, string table, string[] columns, Func<TEntity, int, object> getValue, System.Data.DbType[]? columnTypes)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (columns.Length == 0) throw new ArgumentException("A bulk insert needs at least one column.", nameof(columns));

        Schema = schema;
        Table = table ?? throw new ArgumentNullException(nameof(table));
        // Defensive copy: definitions are cached as static fields and shared across calls, so a
        // caller-retained array reference must not be able to mutate the column list afterwards.
        Columns = (string[])columns.Clone();
        GetValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        ColumnTypes = columnTypes is null ? null : (System.Data.DbType[])columnTypes.Clone();
    }

    /// <summary>Raw schema name, or null for the provider default.</summary>
    public string? Schema { get; }

    /// <summary>Raw (unquoted) table name.</summary>
    public string Table { get; }

    /// <summary>Raw (unquoted) column names, in the accessor's ordinal order.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Ordinal value accessor: (entity, columnOrdinal) → provider value or <see cref="DBNull.Value"/>.</summary>
    public Func<TEntity, int, object> GetValue { get; }

    /// <summary>
    /// Per-column <see cref="System.Data.DbType"/> in the same ordinal order as <see cref="Columns"/>,
    /// or <see langword="null"/> when the provider's bulk-copy API infers types from the destination table.
    /// PostgreSQL's binary COPY protocol requires explicit type annotations; SQL Server and MySQL do not.
    /// </summary>
    public IReadOnlyList<System.Data.DbType>? ColumnTypes { get; }
}
