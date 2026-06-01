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
    /// <param name="suppressSoftDelete">
    /// W8: when true, the soft-delete active filter (<see cref="SoftDeleteActivePredicate"/>) is left
    /// empty so SELECTs built from this context are unfiltered. Used for the per-statement context the
    /// store emitter builds for an <c>IncludeDeleted = true</c> select; the SET clauses are still
    /// computed (they are never suppressed — delete/restore always touch the indicator).
    /// </param>
    public SqlBuildContext(
        SqlBuilder builder,
        string? schema,
        string tableName,
        IReadOnlyList<IColumn> columns,
        bool suppressSoftDelete = false)
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

        // W8 soft delete. The single soft-delete column (if any) drives three precomputed fragments —
        // the active-row filter every SELECT AND-composes (suppressed for IncludeDeleted), and the SET
        // clauses for the soft-delete and restore UPDATEs — so providers consume strings and never
        // reimplement the dialect literals.
        var softDeleteColumn = columns.FirstOrDefault(c => c.SoftDelete != SoftDeleteKind.None);
        if (softDeleteColumn is not null)
        {
            var quoted = builder.QuoteIdentifier(softDeleteColumn.ColumnName);
            if (softDeleteColumn.SoftDelete == SoftDeleteKind.BooleanFlag)
            {
                SoftDeleteActivePredicate = suppressSoftDelete ? string.Empty : quoted + " = " + builder.SoftDeleteFalseLiteral;
                SoftDeleteSetClause = quoted + " = " + builder.SoftDeleteTrueLiteral;
                SoftDeleteRestoreSetClause = quoted + " = " + builder.SoftDeleteFalseLiteral;
            }
            else
            {
                SoftDeleteActivePredicate = suppressSoftDelete ? string.Empty : quoted + " IS NULL";
                SoftDeleteSetClause = quoted + " = " + builder.CurrentTimestampExpression;
                SoftDeleteRestoreSetClause = quoted + " = NULL";
            }
        }
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

    /// <summary>
    /// W8: the active-row filter (<c>"IsDeleted" = 0</c> / <c>"DeletedAt" IS NULL</c>) every SELECT
    /// AND-composes via <see cref="SqlBuilder.AppendWhere"/>. Empty when the entity has no soft-delete
    /// column or this context was built with soft-delete suppressed (IncludeDeleted).
    /// </summary>
    public string SoftDeleteActivePredicate { get; } = string.Empty;

    /// <summary>W8: the SET-clause body that marks a row deleted (<c>"IsDeleted" = 1</c> / <c>"DeletedAt" = CURRENT_TIMESTAMP</c>). Empty when no soft-delete column.</summary>
    public string SoftDeleteSetClause { get; } = string.Empty;

    /// <summary>W8: the SET-clause body that restores a row (<c>"IsDeleted" = 0</c> / <c>"DeletedAt" = NULL</c>). Empty when no soft-delete column.</summary>
    public string SoftDeleteRestoreSetClause { get; } = string.Empty;
}
