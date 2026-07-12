using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Inquiry.SqlServer.Analyzer;

internal sealed class SqlServerSqlBuilder : SqlBuilder
{
    public override CollectionElementExpression BuildCollectionElementExpression(CollectionElementExpressionContext context)
        => context.ProviderSpecialType switch
        {
            Microsoft.CodeAnalysis.SpecialType.System_SByte => new($"unchecked((global::System.Byte)({context.ValueExpression}))", "global::System.Byte", true),
            Microsoft.CodeAnalysis.SpecialType.System_UInt16 => new($"unchecked((global::System.Int16)({context.ValueExpression}))", "global::System.Int16", true),
            Microsoft.CodeAnalysis.SpecialType.System_UInt32 => new($"unchecked((global::System.Int32)({context.ValueExpression}))", "global::System.Int32", true),
            Microsoft.CodeAnalysis.SpecialType.System_UInt64 => new($"unchecked((global::System.Int64)({context.ValueExpression}))", "global::System.Int64", true),
            _ => new(context.ValueExpression, context.ProviderTypeName, false),
        };

    public override string DialectName => "SqlServer";

    protected override string CountExpression => "COUNT_BIG(*)";

    /// <summary>
    /// SQL Server keys its plan cache on the <c>sp_executesql</c> parameter signature, so generated
    /// binders emit <c>Size</c>/<c>Precision</c>/<c>Scale</c> on declared string/decimal parameters to
    /// keep that signature stable across value lengths. See <see cref="SqlBuilder.EmitsParameterSizePrecision"/>.
    /// </summary>
    public override bool EmitsParameterSizePrecision => true;

    /// <summary>
    /// SQL Server binds IN collections as table-valued parameters (TVPs): the SQL stays
    /// <c>col IN (SELECT [Value] FROM @name)</c> for every list length, so prepared statements stay
    /// reusable and the per-element parameter cap does not apply to IN lists — the SQL Server
    /// counterpart of PostgreSQL's <c>= ANY(@array)</c>.
    /// </summary>
    public override bool UseArrayInParameters => true;

    /// <inheritdoc cref="UseArrayInParameters"/>
    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
        => quotedColumn + " IN (SELECT [Value] FROM " + parameterName + ")";

    /// <inheritdoc />
    public override string ArrayParameterBinderFqn => "global::Inquiry.SqlServer.Parameters.InquiryTvpParameter";

    public override CollectionParameterArtifact? BuildCollectionParameterArtifact(string? owningSchema, IColumn column)
    {
        var elementSignature = column.TypeClass switch
        {
            DbTypeClass.Boolean => "bit",
            DbTypeClass.Byte => "tinyint",
            DbTypeClass.Int16 => "smallint",
            DbTypeClass.Int32 => "int",
            DbTypeClass.Int64 => "bigint",
            DbTypeClass.Single => "real",
            DbTypeClass.Double => "float",
            DbTypeClass.Decimal => "decimal(18,2)",
            DbTypeClass.String => "nvarchar(max)",
            DbTypeClass.Guid => "uniqueidentifier",
            DbTypeClass.DateTime => "datetime2",
            DbTypeClass.DateTimeOffset => "datetimeoffset",
            _ => null,
        };
        if (elementSignature is null) return null;

        var schema = string.IsNullOrWhiteSpace(owningSchema) ? "dbo" : owningSchema!;
        var canonicalSignature = "sqlserver-tvp-v1|element=" + elementSignature;
        string hash;
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalSignature));
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            hash = hex.ToString();
        }

        var name = "Inquiry_Tvp_" + hash;
        var quotedSchema = QuoteIdentifier(schema);
        var quotedName = QuoteIdentifier(name);
        var qualified = quotedSchema + "." + quotedName;
        var validationName = qualified;
        var escapedValidation = validationName.Replace("'", "''");
        var createType = $"CREATE TYPE {qualified} AS TABLE ([Value] {elementSignature.ToUpperInvariant()} NOT NULL)";
        var escapedCreateType = createType.Replace("'", "''");
        var schemaDdl = string.IsNullOrWhiteSpace(owningSchema)
            ? string.Empty
            : $"IF SCHEMA_ID(N'{schema.Replace("'", "''")}') IS NULL EXEC(N'CREATE SCHEMA {quotedSchema.Replace("'", "''")}');\n";
        var ddl = $"IF TYPE_ID(N'{escapedValidation}') IS NULL EXEC(N'{escapedCreateType}');";

        return new CollectionParameterArtifact(
            "sqlserver-tvp-v1|schema=" + schema + "|element=" + elementSignature,
            schema,
            name,
            qualified,
            schemaDdl,
            ddl,
            validationName,
            elementSignature);
    }

    public override string QuoteIdentifier(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

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
        var declare = DeclareOutputTable(context);
        var outputInto = " OUTPUT " + InsertedColumns(context) + " INTO @_out";
        var trailing = SelectFromOutput(context);

        if (context.InsertableColumns.Count == 0)
        {
            return declare + " INSERT INTO " + context.Table
                + outputInto
                + " DEFAULT VALUES; " + trailing;
        }

        return declare + " INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ")" + outputInto
            + " VALUES (" + context.InsertParameters + "); " + trailing;
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => DeclareOutputTable(context)
            + " UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " OUTPUT " + InsertedColumns(context) + " INTO @_out"
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause)
            + "; " + SelectFromOutput(context);

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: false);
        }

        if (context.SetClauses.Length == 0)
        {
            return "BEGIN TRANSACTION; " +
                "IF NOT EXISTS (SELECT 1 FROM " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) WHERE " + context.KeyWhereClause + ") " +
                "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + "); " +
                "COMMIT TRANSACTION;";
        }

        return
            "BEGIN TRANSACTION; " +
            "UPDATE " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) SET " + context.SetClauses + " WHERE " + context.KeyWhereClause + "; " +
            "IF @@ROWCOUNT = 0 " +
            "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + "); " +
            "COMMIT TRANSACTION;";
    }

    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context, returning: true);
        }

        if (context.SetClauses.Length == 0)
        {
            return DeclareOutputTable(context) + " " +
                "BEGIN TRANSACTION; " +
                "IF NOT EXISTS (SELECT 1 FROM " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) WHERE " + context.KeyWhereClause + ") " +
                "INSERT INTO " + context.Table + " (" + context.InsertColumns + ")" +
                " OUTPUT " + InsertedColumns(context) + " INTO @_out" +
                " VALUES (" + context.InsertParameters + "); " +
                "COMMIT TRANSACTION; " +
                SelectFromOutput(context);
        }

        return
            DeclareOutputTable(context) + " " +
            "BEGIN TRANSACTION; " +
            "UPDATE " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) SET " + context.SetClauses +
            " OUTPUT " + InsertedColumns(context) + " INTO @_out" +
            " WHERE " + context.KeyWhereClause + "; " +
            "IF @@ROWCOUNT = 0 " +
            "INSERT INTO " + context.Table + " (" + context.InsertColumns + ")" +
            " OUTPUT " + InsertedColumns(context) + " INTO @_out" +
            " VALUES (" + context.InsertParameters + "); " +
            "COMMIT TRANSACTION; " +
            SelectFromOutput(context);
    }

    /// <summary>
    /// SQL Server offset pagination uses the ANSI <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> form,
    /// which requires a preceding ORDER BY (enforced in the generator for all dialects).
    /// </summary>
    public override string BuildPaginationClause(SqlSelectOptions options)
        => "OFFSET " + options.OffsetParameter + " ROWS FETCH NEXT " + options.LimitParameter + " ROWS ONLY";

    protected override string TopOneSuffix => "OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

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

    // OUTPUT INTO @_out requires a typed table variable. Declare it with the entity's column types so the
    // OUTPUT clause works on tables with DML triggers (bare OUTPUT without INTO raises error 334 on
    // triggered tables).
    private string DeclareOutputTable(SqlBuildContext context)
        => "DECLARE @_out TABLE (" + string.Join(", ", context.Columns.Select(c =>
            QuoteIdentifier(c.ColumnName) + " " + MapColumnType(c))) + ");";

    private string SelectFromOutput(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM @_out";

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var output = returning ? " OUTPUT " + InsertedColumns(context) + " INTO @_out" : string.Empty;

        var generatedInsert = context.InsertableColumns.Count == 0
            ? "INSERT INTO " + context.Table + output + " DEFAULT VALUES; "
            : "INSERT INTO " + context.Table + " (" + context.InsertColumns + ")" + output + " VALUES (" + context.InsertParameters + "); ";

        var explicitInsertCols = context.InsertableColumns.Count == 0
            ? keyColumn
            : JoinSql(keyColumn, context.InsertColumns);
        var explicitInsertParams = context.InsertableColumns.Count == 0
            ? keyParameter
            : JoinSql(keyParameter, context.InsertParameters);

        var isIdentity = context.KeyColumns[0].IsGenerated && context.KeyColumns[0].TypeClass != DbTypeClass.Guid;
        var identityOn = isIdentity ? "SET IDENTITY_INSERT " + context.Table + " ON; " : string.Empty;
        var identityOff = isIdentity ? " SET IDENTITY_INSERT " + context.Table + " OFF;" : string.Empty;

        var declare = returning ? DeclareOutputTable(context) + " " : string.Empty;
        var trailing = returning ? " " + SelectFromOutput(context) : string.Empty;

        string elseBranch;
        if (context.SetClauses.Length == 0)
        {
            elseBranch =
                "BEGIN TRANSACTION; " +
                "IF NOT EXISTS (SELECT 1 FROM " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) WHERE " + keyColumn + " = " + keyParameter + ") " +
                "INSERT INTO " + context.Table + " (" + explicitInsertCols + ")" + output + " VALUES (" + explicitInsertParams + "); " +
                "COMMIT TRANSACTION; ";
        }
        else
        {
            elseBranch =
                "BEGIN TRANSACTION; " +
                "UPDATE " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) SET " + context.SetClauses + output + " WHERE " + keyColumn + " = " + keyParameter + "; " +
                "IF @@ROWCOUNT = 0 " +
                "INSERT INTO " + context.Table + " (" + explicitInsertCols + ")" + output + " VALUES (" + explicitInsertParams + "); " +
                "COMMIT TRANSACTION; ";
        }

        return
            declare +
            "IF " + keyParameter + " IS NULL " +
            "BEGIN " +
            generatedInsert +
            "END " +
            "ELSE " +
            "BEGIN " +
            identityOn +
            elseBranch +
            identityOff +
            "END" +
            trailing;
    }

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    // SQL Server cannot key on NVARCHAR(MAX); a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    // nvarchar tops out at 4000 chars, varchar at 8000; a longer declared Length maps to NVARCHAR(MAX) /
    // VARCHAR(MAX), which cannot be keyed or indexed (see MapColumnType).
    protected override int MaxBoundedStringLength(bool isUnicode) => isUnicode ? 4000 : 8000;

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
        // A declared Length beyond the fixed-width ceiling (nvarchar 4000 / varchar 8000) is not a legal
        // bounded type — NVARCHAR(5000) is a DDL error — so it maps to the MAX type instead of emitting
        // invalid SQL. For a regular column that yields valid DDL; for a string KEY or indexed column the
        // MAX type cannot be keyed/indexed, which INQ031/INQ032 now report (the over-ceiling case is folded
        // into MapsToUnboundedString via MaxBoundedStringLength).
        _ => column.Length > 0 && column.Length <= MaxBoundedStringLength(column.IsUnicode)
            ? (column.IsUnicode ? "NVARCHAR(" + column.Length + ")" : "VARCHAR(" + column.Length + ")")
            : (column.IsUnicode ? "NVARCHAR(MAX)" : "VARCHAR(MAX)"),
    };

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " IDENTITY(1,1) PRIMARY KEY";

    // The shared feature catalog uses the ANSI concatenation operator in computed expressions.
    // SQL Server spells string concatenation with +.
    protected override string RenderComputedColumn(IColumn column)
        => "AS (" + TranslateConcatenationOperators(column.ComputedExpression!) + ")";

    private static string TranslateConcatenationOperators(string expression)
    {
        var result = new StringBuilder(expression.Length);
        var state = SqlLexicalState.Normal;
        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            var next = index + 1 < expression.Length ? expression[index + 1] : '\0';
            switch (state)
            {
                case SqlLexicalState.Normal:
                    if (current == '\'' || current == '"' || current == '[')
                    {
                        state = current == '\'' ? SqlLexicalState.SingleQuoted
                            : current == '"' ? SqlLexicalState.DoubleQuoted
                            : SqlLexicalState.Bracketed;
                        result.Append(current);
                    }
                    else if (current == '-' && next == '-')
                    {
                        state = SqlLexicalState.LineComment;
                        result.Append("--");
                        index++;
                    }
                    else if (current == '/' && next == '*')
                    {
                        state = SqlLexicalState.BlockComment;
                        result.Append("/*");
                        index++;
                    }
                    else if (current == '|' && next == '|')
                    {
                        result.Append('+');
                        index++;
                    }
                    else result.Append(current);
                    break;

                case SqlLexicalState.SingleQuoted:
                    result.Append(current);
                    if (current == '\'' && next == '\'') { result.Append(next); index++; }
                    else if (current == '\'') state = SqlLexicalState.Normal;
                    break;

                case SqlLexicalState.DoubleQuoted:
                    result.Append(current);
                    if (current == '"' && next == '"') { result.Append(next); index++; }
                    else if (current == '"') state = SqlLexicalState.Normal;
                    break;

                case SqlLexicalState.Bracketed:
                    result.Append(current);
                    if (current == ']' && next == ']') { result.Append(next); index++; }
                    else if (current == ']') state = SqlLexicalState.Normal;
                    break;

                case SqlLexicalState.LineComment:
                    result.Append(current);
                    if (current is '\r' or '\n') state = SqlLexicalState.Normal;
                    break;

                case SqlLexicalState.BlockComment:
                    result.Append(current);
                    if (current == '*' && next == '/') { result.Append(next); index++; state = SqlLexicalState.Normal; }
                    break;
            }
        }

        return result.ToString();
    }

    private enum SqlLexicalState
    {
        Normal,
        SingleQuoted,
        DoubleQuoted,
        Bracketed,
        LineComment,
        BlockComment,
    }

    protected override string WrapCreateTable(SqlBuildContext context, string body)
    {
        var name = string.IsNullOrEmpty(context.RawSchema)
            ? QuoteIdentifier(context.RawTableName)
            : QuoteIdentifier(context.RawSchema!) + "." + QuoteIdentifier(context.RawTableName);
        return "IF OBJECT_ID(N'" + name.Replace("'", "''") + "', N'U') IS NULL\nBEGIN\n    CREATE TABLE " + context.Table + " (\n        " + body + "\n    );\nEND;";
    }

    // SQL Server extracts a JSON scalar with JSON_VALUE (returns the value as text).
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "JSON_VALUE(" + quotedColumn + ", '" + jsonPath + "')";
}
