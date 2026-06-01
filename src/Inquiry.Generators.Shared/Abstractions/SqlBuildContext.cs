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
        bool suppressSoftDelete = false,
        bool generateForeignKeys = true)
    {
        Columns = columns;
        RawSchema = schema;
        RawTableName = tableName;
        GenerateForeignKeys = generateForeignKeys;
        KeyColumns = columns.Where(c => c.IsKey).ToArray();
        // W6: a database-managed token (rowversion) is supplied by the database, so exclude it from INSERT.
        InsertableColumns = columns.Where(c => !c.IsGenerated && !c.UseDatabaseDefault && !c.IsDatabaseGeneratedToken).ToArray();
        Table = builder.QuoteTable(schema, tableName);
        SelectColumns = string.Join(", ", columns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertColumns = string.Join(", ", InsertableColumns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertParameters = string.Join(", ", InsertableColumns.Select(c => builder.ParameterName(c.PropertyName)));
        // W6: the concurrency token is never SET to a parameter — a DB-managed token is advanced by the
        // database, and an ORM-managed numeric token is advanced via ConcurrencyVersionSet (+1), not @token.
        SetClauses = string.Join(", ", columns
            .Where(c => !c.IsKey && !c.IsGenerated && !c.IsConcurrencyToken)
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

        // W6 optimistic concurrency. The single token column (if any) drives the WHERE predicate every
        // UPDATE/DELETE AND-composes (against the original value, @token) and — for the ORM-managed form
        // — the SET fragment that bumps the version. SetClausesWithVersion is what provider UPDATE methods
        // consume in place of SetClauses, so the +1 bump is uniform across dialects.
        ConcurrencyToken = columns.FirstOrDefault(c => c.IsConcurrencyToken);
        if (ConcurrencyToken is not null)
        {
            var quoted = builder.QuoteIdentifier(ConcurrencyToken.ColumnName);
            ConcurrencyWhereClause = quoted + " = " + builder.ParameterName(ConcurrencyToken.PropertyName);
            if (ConcurrencyToken.IsDatabaseGeneratedToken)
            {
                // DB-managed token: the database advances it, so the ORM never SETs it.
                SetClausesWithVersion = SetClauses;
            }
            else
            {
                ConcurrencyVersionSet = quoted + " = " + quoted + " + 1";
                SetClausesWithVersion = SetClauses.Length == 0
                    ? ConcurrencyVersionSet
                    : SetClauses + ", " + ConcurrencyVersionSet;
            }
        }
        else
        {
            SetClausesWithVersion = SetClauses;
        }
    }

    public string Table { get; }

    /// <summary>W7: the raw (unquoted) table name, for idempotency wrappers like SQL Server's <c>OBJECT_ID(N'…')</c>.</summary>
    public string RawTableName { get; }

    /// <summary>W7: the raw (unquoted) schema name, or null.</summary>
    public string? RawSchema { get; }

    /// <summary>W7: whether <c>BuildCreateTableSql</c> should emit FOREIGN KEY constraints.</summary>
    public bool GenerateForeignKeys { get; }

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

    /// <summary>W6: the entity's single concurrency-token column, or null when none is declared.</summary>
    public IColumn? ConcurrencyToken { get; }

    /// <summary>
    /// W6: the concurrency predicate (<c>"Version" = @Version</c>) every UPDATE/DELETE AND-composes via
    /// <see cref="SqlBuilder.AppendWhere"/> onto the key WHERE. Empty when the entity has no token.
    /// </summary>
    public string ConcurrencyWhereClause { get; } = string.Empty;

    /// <summary>
    /// W6: the SET fragment that bumps an ORM-managed numeric token (<c>"Version" = "Version" + 1</c>).
    /// Empty when there is no token or the token is database-managed (the database advances it).
    /// </summary>
    public string ConcurrencyVersionSet { get; } = string.Empty;

    /// <summary>
    /// W6: <see cref="SetClauses"/> plus the version bump for an ORM-managed token. Provider UPDATE
    /// methods consume this instead of <see cref="SetClauses"/>. Identical to <see cref="SetClauses"/>
    /// when there is no token or it is database-managed.
    /// </summary>
    public string SetClausesWithVersion { get; }
}
