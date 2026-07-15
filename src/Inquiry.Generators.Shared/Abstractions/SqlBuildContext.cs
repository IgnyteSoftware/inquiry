using System;
using System.Collections.Generic;
using System.Linq;
using Inquiry.Generators.Models;

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
    private readonly IReadOnlyList<string> _activeRowPredicateTerms;

    internal ISet<string>? SuppressedForeignKeyColumns { get; init; }
    internal IReadOnlyList<ForeignKeyConstraintData>? NormalizedForeignKeys { get; init; }
    internal IReadOnlyList<IndexData>? NormalizedIndexes { get; init; }
    internal IReadOnlyList<CheckConstraintData>? NormalizedChecks { get; init; }
    /// <param name="suppressSoftDelete">
    /// When true, the soft-delete term is dropped from <see cref="ActiveRowPredicate"/> so SELECTs built
    /// from this context are not filtered by the soft-delete indicator (any global-filter terms still
    /// apply). Used for the per-statement context the store emitter builds for an <c>IncludeDeleted =
    /// true</c> select; the SET clauses are still computed (they are never suppressed — delete/restore
    /// always touch the indicator).
    /// </param>
    /// <param name="softDeletePredicateColumn">
    /// The entity's soft-delete column, supplied only when <paramref name="columns"/> does not itself
    /// carry one. This is the projection case: a projection's column list is a subset of the entity's
    /// columns and never includes the soft-delete indicator, so the active-row filter would be silently
    /// dropped. Passing the entity's soft-delete column here composes the filter into the projection
    /// SELECT (the column does not join the SELECT list — <see cref="SelectColumns"/> is built from
    /// <paramref name="columns"/> only). Null (the default) for entity contexts, which already detect
    /// their soft-delete column from <paramref name="columns"/>.
    /// </param>
    /// <param name="globalFilterPredicateColumns">
    /// The entity's <c>[InquiryGlobalFilter]</c> columns, supplied for the same reason as
    /// <paramref name="softDeletePredicateColumn"/> — a projection's subset omits them, so they are
    /// passed explicitly to keep the active-row filter intact. Null (the default) for entity contexts,
    /// which detect their global-filter columns from <paramref name="columns"/>.
    /// </param>
    /// <param name="hasSecondaryUniqueConstraint">
    /// Whether the entity declares any unique constraint beyond its primary key. Providers whose
    /// upsert-returning emulation cannot identify a row after a secondary-unique conflict use this
    /// metadata to degrade that operation instead of returning the wrong row.
    /// </param>
    public SqlBuildContext(
        SqlBuilder builder,
        string? schema,
        string tableName,
        IReadOnlyList<IColumn> columns,
        bool suppressSoftDelete = false,
        bool generateForeignKeys = true,
        IColumn? softDeletePredicateColumn = null,
        IReadOnlyList<IColumn>? globalFilterPredicateColumns = null,
        bool hasSecondaryUniqueConstraint = false)
    {
        Columns = columns;
        RawSchema = schema;
        RawTableName = tableName;
        GenerateForeignKeys = generateForeignKeys;
        HasSecondaryUniqueConstraint = hasSecondaryUniqueConstraint;
        KeyColumns = columns.Where(c => c.IsKey).ToArray();
        // A database-managed token (rowversion) is supplied by the database, so exclude it from INSERT.
        // A server-computed column is calculated by the database expression, so exclude it too.
        InsertableColumns = columns.Where(c => !c.IsGenerated && !c.UseDatabaseDefault && !c.IsDatabaseGeneratedToken && string.IsNullOrEmpty(c.ComputedExpression)).ToArray();
        Table = builder.QuoteTable(schema, tableName);
        SelectColumns = string.Join(", ", columns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertColumns = string.Join(", ", InsertableColumns.Select(c => builder.QuoteIdentifier(c.ColumnName)));
        InsertParameters = string.Join(", ", InsertableColumns.Select(c => builder.ParameterName(c.PropertyName)));
        // The concurrency token is never SET to a parameter — a DB-managed token is advanced by the
        // database, and an ORM-managed numeric token is advanced via ConcurrencyVersionSet (+1), not @token.
        // A created-* auditing column ([InquiryCreatedAt]/[InquiryCreatedBy]) is never SET either:
        // it is written once by INSERT and must survive every subsequent UPDATE / upsert conflict
        // branch unchanged.
        // A server-computed column is never SET — the database recomputes it from its expression.
        SetClauses = string.Join(", ", columns
            .Where(c => !c.IsKey && !c.IsGenerated && !c.IsConcurrencyToken && !c.IsCreatedAt && !c.IsCreatedBy && string.IsNullOrEmpty(c.ComputedExpression))
            .Select(c => builder.QuoteIdentifier(c.ColumnName) + " = " + builder.ParameterName(c.PropertyName)));
        QuotedKeyColumns = KeyColumns.Select(k => builder.QuoteIdentifier(k.ColumnName)).ToArray();
        KeyParameters = KeyColumns.Select(k => builder.ParameterName(k.PropertyName)).ToArray();
        KeyWhereClause = string.Join(" AND ", KeyColumns
            .Select(k => builder.QuoteIdentifier(k.ColumnName) + " = " + builder.ParameterName(k.PropertyName)));

        // Active-row filter. The active-row predicate every SELECT AND-composes is built from two
        // sources: the single soft-delete column (active = not-deleted) and any [InquiryGlobalFilter]
        // columns (active = matches KeepWhen). Both are precomputed into one string so providers consume
        // it and never reimplement the dialect literals.
        // Entity contexts find these columns in `columns`. Projection contexts don't carry them (the
        // projection selects a subset that omits indicator/filter columns), so they are supplied
        // explicitly — used only for the predicate, never added to SelectColumns.
        var activeRowPredicates = new List<string>();
        var qualifiedActiveRowPredicates = new List<string>();

        // Soft delete. Drives the active-row filter (suppressed for IncludeDeleted) plus the SET clauses
        // for the soft-delete and restore UPDATEs.
        var softDeleteColumn = columns.FirstOrDefault(c => c.SoftDelete != SoftDeleteKind.None) ?? softDeletePredicateColumn;
        if (softDeleteColumn is not null)
        {
            var quoted = builder.QuoteIdentifier(softDeleteColumn.ColumnName);
            if (softDeleteColumn.SoftDelete == SoftDeleteKind.BooleanFlag)
            {
                if (!suppressSoftDelete)
                {
                    activeRowPredicates.Add(quoted + " = " + builder.BooleanFalseLiteral);
                    qualifiedActiveRowPredicates.Add(Table + "." + quoted + " = " + builder.BooleanFalseLiteral);
                }
                SoftDeleteSetClause = quoted + " = " + builder.BooleanTrueLiteral;
                SoftDeleteRestoreSetClause = quoted + " = " + builder.BooleanFalseLiteral;
            }
            else
            {
                if (!suppressSoftDelete)
                {
                    activeRowPredicates.Add(quoted + " IS NULL");
                    qualifiedActiveRowPredicates.Add(Table + "." + quoted + " IS NULL");
                }
                var timestampExpr = softDeleteColumn.TypeClass == DbTypeClass.DateTimeOffset
                    ? builder.CurrentTimestampOffsetExpression
                    : builder.CurrentTimestampExpression;
                SoftDeleteSetClause = quoted + " = " + timestampExpr;
                SoftDeleteRestoreSetClause = quoted + " = NULL";
            }
        }

        // Global filters. Always applied — unlike soft delete, there is no per-method opt-out, so they
        // are not suppressed by IncludeDeleted (an "include deleted" read still respects tenant filtering).
        var globalFilterColumns = columns.Where(c => c.IsGlobalFilter).Concat(globalFilterPredicateColumns ?? Array.Empty<IColumn>());
        foreach (var gf in globalFilterColumns)
        {
            var gfQuoted = builder.QuoteIdentifier(gf.ColumnName);
            var gfValue = gf.GlobalFilterKeepWhenTrue ? builder.BooleanTrueLiteral : builder.BooleanFalseLiteral;
            activeRowPredicates.Add(gfQuoted + " = " + gfValue);
            qualifiedActiveRowPredicates.Add(Table + "." + gfQuoted + " = " + gfValue);
        }

        ActiveRowPredicate = string.Join(" AND ", activeRowPredicates);
        QualifiedActiveRowPredicate = string.Join(" AND ", qualifiedActiveRowPredicates);
        _activeRowPredicateTerms = activeRowPredicates;

        // Optimistic concurrency. The single token column (if any) drives the WHERE predicate every
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

    /// <summary>The raw (unquoted) table name, for idempotency wrappers like SQL Server's <c>OBJECT_ID(N'…')</c>.</summary>
    public string RawTableName { get; }

    /// <summary>The raw (unquoted) schema name, or null.</summary>
    public string? RawSchema { get; }

    /// <summary>Whether <c>BuildCreateTableSql</c> should emit FOREIGN KEY constraints.</summary>
    public bool GenerateForeignKeys { get; }

    /// <summary>Whether the entity declares a unique constraint other than its primary key.</summary>
    public bool HasSecondaryUniqueConstraint { get; }

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
    /// The active-row filter every SELECT AND-composes via <see cref="SqlBuilder.AppendWhere"/>: the
    /// soft-delete active condition (<c>"IsDeleted" = 0</c> / <c>"DeletedAt" IS NULL</c>) AND every
    /// <c>[InquiryGlobalFilter]</c> condition (<c>"IsActive" = 1</c>). Empty when the entity has neither.
    /// The soft-delete term is dropped when this context was built with soft-delete suppressed
    /// (IncludeDeleted); global-filter terms always remain.
    /// </summary>
    public string ActiveRowPredicate { get; } = string.Empty;

    /// <summary>
    /// Table-qualified variant of <see cref="ActiveRowPredicate"/> for use in multi-table queries
    /// (e.g. many-to-many JOINs) where an unqualified column name would be ambiguous.
    /// </summary>
    public string QualifiedActiveRowPredicate { get; } = string.Empty;

    /// <summary>
    /// Returns <see cref="ActiveRowPredicate"/> qualified by an already-quoted table or alias.
    /// This keeps aliased joins structural: callers supply a dialect-quoted qualifier and never
    /// rewrite the generated predicate text.
    /// </summary>
    internal string QualifyActiveRowPredicate(string quotedQualifier)
        => string.Join(" AND ", _activeRowPredicateTerms.Select(term => quotedQualifier + "." + term));

    /// <summary>The SET-clause body that marks a row deleted (<c>"IsDeleted" = 1</c> / <c>"DeletedAt" = CURRENT_TIMESTAMP</c>). Empty when no soft-delete column.</summary>
    public string SoftDeleteSetClause { get; } = string.Empty;

    /// <summary>The SET-clause body that restores a row (<c>"IsDeleted" = 0</c> / <c>"DeletedAt" = NULL</c>). Empty when no soft-delete column.</summary>
    public string SoftDeleteRestoreSetClause { get; } = string.Empty;

    /// <summary>The entity's single concurrency-token column, or null when none is declared.</summary>
    public IColumn? ConcurrencyToken { get; }

    /// <summary>
    /// The concurrency predicate (<c>"Version" = @Version</c>) every UPDATE/DELETE AND-composes via
    /// <see cref="SqlBuilder.AppendWhere"/> onto the key WHERE. Empty when the entity has no token.
    /// </summary>
    public string ConcurrencyWhereClause { get; } = string.Empty;

    /// <summary>
    /// The SET fragment that bumps an ORM-managed numeric token (<c>"Version" = "Version" + 1</c>).
    /// Empty when there is no token or the token is database-managed (the database advances it).
    /// </summary>
    public string ConcurrencyVersionSet { get; } = string.Empty;

    /// <summary>
    /// <see cref="SetClauses"/> plus the version bump for an ORM-managed token. Provider UPDATE
    /// methods consume this instead of <see cref="SetClauses"/>. Identical to <see cref="SetClauses"/>
    /// when there is no token or it is database-managed.
    /// </summary>
    public string SetClausesWithVersion { get; }
}
