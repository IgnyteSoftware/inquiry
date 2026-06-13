using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.SqlServer.Analyzer;

internal sealed class SqlServerSqlBuilder : SqlBuilder
{
    public override string DialectName => "SqlServer";

    public override string QuoteIdentifier(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    public override string BuildSelectAllSql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + WhereSuffix(context.ActiveRowPredicate);

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns)
    {
        var where = string.Join(" AND ", filterColumns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(where, context.ActiveRowPredicate);
    }

    /// <summary>SQL Server uses <c>GETUTCDATE()</c> for the soft-delete (and restore) timestamp clock.</summary>
    public override string CurrentTimestampExpression => "GETUTCDATE()";

    public override bool SupportsFullTextSearch => true;

    /// <summary>SQL Server bulk inserts ride SqlBulkCopy via the provider-registered copier.</summary>
    public override bool SupportsBulkCopy => true;

    public override string BuildFullTextSearchSql(SqlBuildContext context, IReadOnlyList<IColumn> searchColumns)
    {
        // FREETEXT does natural-language matching over the searched columns (requires a full-text index).
        var cols = string.Join(", ", searchColumns.Select(c => QuoteIdentifier(c.ColumnName)));
        var predicate = "FREETEXT((" + cols + "), " + ParameterName("searchTerm") + ")";
        return "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(predicate, context.ActiveRowPredicate);
    }

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
    {
        if (context.InsertableColumns.Count == 0)
        {
            return "INSERT INTO " + context.Table
                + " OUTPUT " + InsertedColumns(context)
                + " DEFAULT VALUES";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") OUTPUT " + InsertedColumns(context)
            + " VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " OUTPUT " + InsertedColumns(context)
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        return
            "MERGE INTO " + context.Table + " WITH (HOLDLOCK) AS target " +
            "USING (" + BuildSourceSelect(context) + ") AS source ON " + BuildSourceJoin(context) + " " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ");";
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        return
            "MERGE INTO " + context.Table + " WITH (HOLDLOCK) AS target " +
            "USING (" + BuildSourceSelect(context) + ") AS source ON " + BuildSourceJoin(context) + " " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "OUTPUT " + InsertedColumns(context) + ";";
    }

    /// <summary>
    /// SQL Server offset pagination uses the ANSI <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> form,
    /// which requires a preceding ORDER BY (enforced in the generator for all dialects).
    /// </summary>
    public override string BuildPaginationClause(SqlSelectOptions options)
        => "OFFSET " + options.OffsetParameter + " ROWS FETCH NEXT " + options.LimitParameter + " ROWS ONLY";

    /// <summary>
    /// SQL Server lacks the row-value <c>(a, b) &gt; (@c0, @c1)</c> comparison, so a multi-column keyset
    /// renders the lexicographic OR-form <c>(a &gt; @c0) OR (a = @c0 AND b &gt; @c1)</c>. Single-column
    /// keysets fall back to the portable scalar form.
    /// </summary>
    public override string BuildKeysetPredicate(SqlSelectOptions options)
    {
        if (options.KeysetColumns.Count == 1)
        {
            return base.BuildKeysetPredicate(options);
        }

        // Bare lexicographic OR-form seek predicate (no IS NULL guard — see SqlBuilder.BuildKeysetPredicate
        // remarks); one outer paren wraps the OR-chain so it AND-composes correctly with a soft-delete filter.
        var op = options.KeysetDescending ? " < " : " > ";
        var sb = new System.Text.StringBuilder();
        sb.Append('(');
        for (var i = 0; i < options.KeysetColumns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" OR ");
            }

            sb.Append('(');
            for (var j = 0; j < i; j++)
            {
                sb.Append(options.KeysetColumns[j]).Append(" = ").Append(options.KeysetCursorParameters[j]).Append(" AND ");
            }

            sb.Append(options.KeysetColumns[i]).Append(op).Append(options.KeysetCursorParameters[i]).Append(')');
        }

        sb.Append(')');
        return sb.ToString();
    }

    private string InsertedColumns(SqlBuildContext context)
        => string.Join(", ", context.Columns.Select(c => "INSERTED." + QuoteIdentifier(c.ColumnName)));

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var output = returning ? " OUTPUT " + InsertedColumns(context) : string.Empty;

        // The null-key fast path always omits the key and lets the database supply it (IDENTITY assigns;
        // a GUID DEFAULT fires).
        var generatedInsert = context.InsertableColumns.Count == 0
            ? "INSERT INTO " + context.Table + output + " DEFAULT VALUES; "
            : "INSERT INTO " + context.Table + " (" + context.InsertColumns + ")" + output + " VALUES (" + context.InsertParameters + "); ";

        // The MERGE's NOT MATCHED INSERT handles a supplied (non-null) key. A database-supplied GUID key
        // (DEFAULT NEWSEQUENTIALID) accepts an explicit value, so the supplied key is written through. An
        // IDENTITY (integer) key cannot — SQL Server rejects an explicit identity insert (error 544) even
        // on a MERGE branch that is never taken — so it inserts only the non-key columns, like the
        // null-key path, and lets the database assign the key.
        string notMatchedInsert;
        if (context.KeyColumns[0].TypeClass == DbTypeClass.Guid)
        {
            notMatchedInsert = "(" + JoinSql(keyColumn, context.InsertColumns) + ") VALUES (" + JoinSql(keyParameter, context.InsertParameters) + ")";
        }
        else
        {
            notMatchedInsert = context.InsertableColumns.Count == 0
                ? "DEFAULT VALUES"
                : "(" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
        }

        return
            "IF " + keyParameter + " IS NULL " +
            "BEGIN " +
            generatedInsert +
            "END " +
            "ELSE " +
            "BEGIN " +
            "MERGE INTO " + context.Table + " WITH (HOLDLOCK) AS target " +
            "USING (SELECT " + keyParameter + " AS k0) AS source ON target." + keyColumn + " = source.k0 " +
            "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
            "WHEN NOT MATCHED THEN INSERT " + notMatchedInsert + output + "; " +
            "END";
    }

    private static string BuildSourceSelect(SqlBuildContext context)
        => "SELECT " + string.Join(", ", context.KeyParameters.Select((p, i) => p + " AS k" + i));

    private static string BuildSourceJoin(SqlBuildContext context)
        => string.Join(" AND ", context.QuotedKeyColumns.Select((q, i) => "target." + q + " = source.k" + i));

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    // SQL Server cannot key on NVARCHAR(MAX); a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean => "BIT",
        DbTypeClass.Byte => "TINYINT",
        DbTypeClass.Int16 => "SMALLINT",
        DbTypeClass.Int32 => "INT",
        DbTypeClass.Int64 => "BIGINT",
        DbTypeClass.Single => "REAL",
        DbTypeClass.Double => "FLOAT",
        DbTypeClass.Decimal => "DECIMAL(" + DecimalSpec(column, 18, 2) + ")",
        DbTypeClass.DateTime => "DATETIME2",
        DbTypeClass.DateTimeOffset => "DATETIMEOFFSET",
        DbTypeClass.DateOnly => "DATE",
        DbTypeClass.TimeOnly => "TIME",
        DbTypeClass.Guid => "UNIQUEIDENTIFIER",
        DbTypeClass.ByteArray => "VARBINARY(MAX)",
        // SQL Server cannot key on NVARCHAR(MAX); a bounded Length is required for PK/FK string columns.
        _ => column.Length > 0 ? "NVARCHAR(" + column.Length + ")" : "NVARCHAR(MAX)",
    };

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " IDENTITY(1,1) PRIMARY KEY";

    protected override string WrapCreateTable(SqlBuildContext context, string body)
    {
        var name = string.IsNullOrEmpty(context.RawSchema) ? context.RawTableName : context.RawSchema + "." + context.RawTableName;
        return "IF OBJECT_ID(N'" + name.Replace("'", "''") + "', N'U') IS NULL\nBEGIN\n    CREATE TABLE " + context.Table + " (\n        " + body + "\n    );\nEND;";
    }

    // SQL Server extracts a JSON scalar with JSON_VALUE (returns the value as text).
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "JSON_VALUE(" + quotedColumn + ", '" + jsonPath + "')";
}
