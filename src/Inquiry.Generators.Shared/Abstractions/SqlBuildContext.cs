using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// Precomputed SQL fragments each <see cref="SqlBuilder"/> needs from an entity's column metadata,
/// so each statement build is a small amount of string concatenation rather than re-running LINQ pipelines.
/// </summary>
/// <remarks>
/// Constructed once per (entity, builder) pair inside the generator and reused for every statement
/// built for that entity. The resulting SQL strings are then emitted as <c>const</c> fields on the
/// generated store, so this work happens at compile time and never at runtime.
/// </remarks>
public sealed class SqlBuildContext
{
    public SqlBuildContext(
        SqlBuilder builder,
        string? schema,
        string tableName,
        IReadOnlyList<IColumn> columns)
    {
        Columns = columns;
        KeyColumns = columns.Where(c => c.IsKey).ToArray();
        InsertableColumns = columns.Where(c => !c.IsGenerated && !c.UseDatabaseDefault).ToArray();
        Table = builder.QuoteTable(schema, tableName);
        SelectColumns = string.Join(", ", columns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertColumns = string.Join(", ", InsertableColumns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertParameters = string.Join(", ", InsertableColumns.Select(c => builder.ParameterName(c.PropertyName)));
        SetClauses = string.Join(", ", columns
            .Where(c => !c.IsKey && !c.IsGenerated)
            .Select(c => builder.QuoteIdentifier(c.ColumnName) + " = " + builder.ParameterName(c.PropertyName)));
        QuotedKeyColumns = KeyColumns.Select(k => builder.QuoteIdentifier(k.ColumnName)).ToArray();
        KeyParameters = KeyColumns.Select(k => builder.ParameterName(k.PropertyName)).ToArray();
        KeyWhereClause = string.Join(" AND ", KeyColumns
            .Select(k => builder.QuoteIdentifier(k.ColumnName) + " = " + builder.ParameterName(k.PropertyName)));
    }

    public string Table { get; }
    public IReadOnlyList<IColumn> Columns { get; }
    public IReadOnlyList<IColumn> KeyColumns { get; }
    public IReadOnlyList<IColumn> InsertableColumns { get; }
    public string SelectColumns { get; }
    public string InsertColumns { get; }
    public string InsertParameters { get; }
    public string SetClauses { get; }
    public IReadOnlyList<string> QuotedKeyColumns { get; }
    public IReadOnlyList<string> KeyParameters { get; }
    public string KeyWhereClause { get; }
}
