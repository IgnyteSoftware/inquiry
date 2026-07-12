using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Sqlite.Analyzer;

internal sealed class SqliteSqlBuilder : SqlBuilder
{
    public override string DialectName => "Sqlite";

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.Inline;
    public override bool SupportsCheckConstraints => true;
    public override ConstraintNameScope ForeignKeyConstraintNameScope => ConstraintNameScope.Table;
    public override ConstraintNameScope CheckConstraintNameScope => ConstraintNameScope.Table;
    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent @event) => action is >= ReferentialActionKind.NoAction and <= ReferentialActionKind.SetDefault;

    public override string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string BuildInsertSql(SqlBuildContext context)
    {
        if (context.InsertableColumns.Count == 0)
        {
            return "INSERT INTO " + context.Table + " DEFAULT VALUES";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildInsertReturningSql(SqlBuildContext context)
        => BuildInsertSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            OnConflictClause(JoinKeyColumns(context), context.SetClauses);
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var explicitInsertColumns = JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParameters = JoinSql(keyParameter, context.InsertParameters);
        var returningClause = returning ? " RETURNING " + context.SelectColumns : string.Empty;

        return "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") VALUES (" + explicitInsertParameters + ") " +
            OnConflictClause(keyColumn, context.SetClauses) + returningClause;
    }

    // An entity with no updatable non-key columns yields an empty SET clause; emit DO NOTHING (a conflict
    // is a valid no-op — "insert if absent") instead of the invalid `DO UPDATE SET ` with an empty body.
    private static string OnConflictClause(string conflictTarget, string setClauses)
        => setClauses.Length == 0
            ? "ON CONFLICT (" + conflictTarget + ") DO NOTHING"
            : "ON CONFLICT (" + conflictTarget + ") DO UPDATE SET " + setClauses;

    private static string JoinKeyColumns(SqlBuildContext context)
        => string.Join(", ", context.QuotedKeyColumns);

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    public override bool UseArrayInParameters => true;

    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
        => quotedColumn + " IN (SELECT value FROM json_each(" + parameterName + "))";

    public override string ArrayParameterBinderFqn => "global::Inquiry.Parameters.InquiryJsonArrayParameter";

    // ---- DDL --------------------------------------------------------------------------------
    // SQLite has dynamic typing; these affinities match the conventional Northwind mapping.

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean or DbTypeClass.Byte or DbTypeClass.Int16 or DbTypeClass.Int32 or DbTypeClass.Int64 => "INTEGER",
        DbTypeClass.Single or DbTypeClass.Double => "REAL",
        DbTypeClass.Decimal => "NUMERIC",
        // SQLite has no date/time storage classes; DateOnly/TimeOnly round-trip as ISO-8601 TEXT.
        DbTypeClass.DateOnly or DbTypeClass.TimeOnly => "TEXT",
        DbTypeClass.ByteArray => "BLOB",
        _ => "TEXT",
    };

    // SQLite's auto-increment rowid alias is always INTEGER PRIMARY KEY AUTOINCREMENT regardless of CLR width.
    protected override string GeneratedKeyClause(IColumn column) => "INTEGER PRIMARY KEY AUTOINCREMENT";

    protected override bool SupportsCreateIndexIfNotExists => true;
}
