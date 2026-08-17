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
        : this(schema, table, columns, getValue, columnTypes: null, fieldTypes: null, typedAccessors: null)
    {
    }

    /// <summary>Initializes the definition with explicit per-column type annotations.</summary>
    /// <param name="schema"><inheritdoc cref="Schema" path="/summary"/></param>
    /// <param name="table"><inheritdoc cref="Table" path="/summary"/></param>
    /// <param name="columns"><inheritdoc cref="Columns" path="/summary"/></param>
    /// <param name="getValue"><inheritdoc cref="GetValue" path="/summary"/></param>
    /// <param name="columnTypes"><inheritdoc cref="ColumnTypes" path="/summary"/></param>
    public InquiryBulkInsertDefinition(string? schema, string table, string[] columns, Func<TEntity, int, object> getValue, System.Data.DbType[]? columnTypes)
        : this(schema, table, columns, getValue, columnTypes, fieldTypes: null, typedAccessors: null)
    {
    }

    /// <summary>Initializes the definition with explicit wire and reader field type annotations.</summary>
    /// <param name="schema"><inheritdoc cref="Schema" path="/summary"/></param>
    /// <param name="table"><inheritdoc cref="Table" path="/summary"/></param>
    /// <param name="columns"><inheritdoc cref="Columns" path="/summary"/></param>
    /// <param name="getValue"><inheritdoc cref="GetValue" path="/summary"/></param>
    /// <param name="columnTypes"><inheritdoc cref="ColumnTypes" path="/summary"/></param>
    /// <param name="fieldTypes"><inheritdoc cref="FieldTypes" path="/summary"/></param>
    public InquiryBulkInsertDefinition(
        string? schema,
        string table,
        string[] columns,
        Func<TEntity, int, object> getValue,
        System.Data.DbType[]? columnTypes,
        Type[]? fieldTypes)
        : this(schema, table, columns, getValue, columnTypes, fieldTypes, typedAccessors: null)
    {
    }

    /// <summary>Initializes the definition with wire, reader, and strongly typed accessor metadata.</summary>
    public InquiryBulkInsertDefinition(
        string? schema,
        string table,
        string[] columns,
        Func<TEntity, int, object> getValue,
        System.Data.DbType[]? columnTypes,
        Type[]? fieldTypes,
        IInquiryBulkColumnAccessor<TEntity>[]? typedAccessors)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (columns.Length == 0) throw new ArgumentException("A bulk insert needs at least one column.", nameof(columns));
        if (columnTypes is not null && columnTypes.Length != columns.Length)
            throw new ArgumentException("Column type metadata must have one entry per column.", nameof(columnTypes));
        if (fieldTypes is not null && fieldTypes.Length != columns.Length)
            throw new ArgumentException("Field type metadata must have one entry per column.", nameof(fieldTypes));
        if (typedAccessors is not null && typedAccessors.Length != columns.Length)
            throw new ArgumentException("Typed accessor metadata must have one entry per column.", nameof(typedAccessors));

        Schema = schema;
        Table = table ?? throw new ArgumentNullException(nameof(table));
        // Defensive copy: definitions are cached as static fields and shared across calls, so a
        // caller-retained array reference must not be able to mutate the column list afterwards.
        Columns = (string[])columns.Clone();
        GetValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        ColumnTypes = columnTypes is null ? null : (System.Data.DbType[])columnTypes.Clone();
        FieldTypes = fieldTypes is null ? null : (Type[])fieldTypes.Clone();
        TypedAccessors = typedAccessors is null ? null : (IInquiryBulkColumnAccessor<TEntity>[])typedAccessors.Clone();
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
    /// PostgreSQL's binary COPY protocol requires explicit wire annotations.
    /// </summary>
    public IReadOnlyList<System.Data.DbType>? ColumnTypes { get; }

    /// <summary>
    /// Exact CLR types returned by <see cref="GetValue"/> after converter, enum, and provider
    /// bridging, in column order. SQL Server and MySQL bulk readers consume this shape before the
    /// first row and when a current value is <see cref="DBNull.Value"/>.
    /// </summary>
    public IReadOnlyList<Type>? FieldTypes { get; }

    /// <summary>Strongly typed per-column accessors emitted by the generator, or null for manual definitions.</summary>
    public IReadOnlyList<IInquiryBulkColumnAccessor<TEntity>>? TypedAccessors { get; }
}
