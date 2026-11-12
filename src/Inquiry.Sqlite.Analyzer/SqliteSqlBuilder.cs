using Inquiry.Generators.Abstractions;

namespace Inquiry.Sqlite.Analyzer;

internal sealed class SqliteSqlBuilder : SqlBuilder
{
    public override BatchInsertStrategy BatchInsertStrategy => BatchInsertStrategy.Row;

    // SQLite 3.32+ defaults SQLITE_MAX_VARIABLE_NUMBER to 32,766.
    public override int HardMaxParametersPerCommand => 32766;

    public override string DialectName => "Sqlite";
    public override string ProviderId => "sqlite";
    // Measured for #509: scalar parameters beat json_each up to 4 values; json_each wins from 8.
    public override int ScalarInListMaxLength => 4;
    // SQLite identifiers compare ASCII case-insensitively by default. Non-ASCII remains exact.
    public override string GetPhysicalIdentifierSortKey(string identifier) => FoldAscii(identifier, upper: true);

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.Inline;
    public override bool SupportsCheckConstraints => true;
    public override ConstraintNameScope ForeignKeyConstraintNameScope => ConstraintNameScope.Table;
    public override ConstraintNameScope CheckConstraintNameScope => ConstraintNameScope.Table;
    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent actionEvent) => action is >= ReferentialActionKind.NoAction and <= ReferentialActionKind.SetDefault;

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
            + " WHERE " + context.KeyUpdateWhereClause;

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;

    internal override string BuildSoftDeleteByKeyReturningSql(SqlBuildContext context)
        => BuildSoftDeleteByKeySql(context) + " RETURNING " + context.SelectColumns;

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWriteWhereClause;

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

    private static string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
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

    // json_each yields text for a JSON string. A BLOB column needs the bytes, which the list carries as hex.
    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
        => quotedColumn + " IN (SELECT " + (elementType == DbTypeClass.ByteArray ? "unhex(value)" : "value")
            + " FROM json_each(" + parameterName + "))";

    // The IN list compares against the text or number that Microsoft.Data.Sqlite stored for a scalar
    // parameter, with no conversion. Each element must therefore take the exact stored form. The JSON
    // transport writes lower-case GUIDs, ISO 8601 timestamps and the shortest float text, which
    // never equal the stored values, so the list matched no row.
    public override CollectionElementExpression BuildCollectionElementExpression(CollectionElementExpressionContext context)
    {
        const string culture = "global::System.Globalization.CultureInfo.InvariantCulture";
        const string text = "global::System.String";
        var value = context.ValueExpression;

        if (context.ProviderSpecialType == Microsoft.CodeAnalysis.SpecialType.System_DateTime)
        {
            return new($"({value}).ToString(\"yyyy'-'MM'-'dd HH':'mm':'ss.FFFFFFF\", {culture})", text, true);
        }

        if (context.ProviderSpecialType == Microsoft.CodeAnalysis.SpecialType.System_Single)
        {
            // The driver stores a float as a double. The widened value keeps the same digits.
            return new($"(global::System.Double)({value})", "global::System.Double", true);
        }

        return context.ProviderTypeName switch
        {
            "global::System.Guid" => new($"({value}).ToString(\"D\").ToUpperInvariant()", text, true),
            "global::System.DateTimeOffset" => new($"({value}).ToString(\"yyyy'-'MM'-'dd HH':'mm':'ss.FFFFFFFzzz\", {culture})", text, true),
            // The driver writes whole seconds without a fraction and any other time with seven digits.
            "global::System.TimeOnly" => new(
                $"(({value}) switch {{ var _t when _t.Ticks % 10000000L == 0 => _t.ToString(\"HH':'mm':'ss\", {culture}), var _t => _t.ToString(\"HH':'mm':'ss.fffffff\", {culture}) }})",
                text,
                true),
            "byte[]" => new($"(({value}) is {{ }} _b ? global::System.Convert.ToHexString(_b) : null)", text, true),
            _ => base.BuildCollectionElementExpression(context),
        };
    }

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

    protected override string BuildLockSuffix(int lockMode)
        => throw new System.NotSupportedException("SQLite does not support row-level locking.");
}
