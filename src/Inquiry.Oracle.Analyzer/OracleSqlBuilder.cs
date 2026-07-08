using Inquiry.Generators.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Inquiry.Oracle.Analyzer;

/// <summary>
/// Oracle 12c+ SQL builder. Three things set Oracle apart from the PostgreSQL/SQL Server shape it
/// otherwise mirrors:
/// <list type="bullet">
/// <item><description>Bind parameters use the <c>:name</c> prefix instead of <c>@name</c>. The SQL
/// text is corrected here via <see cref="ParameterName"/>; the runtime binder still emits
/// <c>@name</c> (shared, unmodified) and <c>OracleCommand.BindByName = true</c> — set in
/// <c>OracleInquiryConnectionFactory.InitializeCommand</c> — matches the two by name.</description></item>
/// <item><description>Identifiers are left <b>unquoted</b>. Oracle folds unquoted identifiers to
/// uppercase, so the Northwind DDL is created unquoted to match; blanket double-quoting would force
/// exact-case names that the unquoted DDL would not resolve. The lone exception is an identifier that is
/// not legal unquoted (e.g. the embedded space in <c>Order Details</c>), which <see cref="QuoteIdentifier"/>
/// double-quotes.</description></item>
/// <item><description><c>RETURNING … INTO</c> binds OUT parameters rather than producing a result set,
/// so <c>ReturnEntity = true</c> ops are emitted as an anonymous PL/SQL block that mutates and OPENs a ref
/// cursor over the affected row; <c>ExecuteReader</c> on the block returns that cursor, which the reader
/// pipeline materializes unchanged (the OUT cursor is bound by <c>OracleInquiryConnectionFactory</c>). See
/// the returning builders below.</description></item>
/// </list>
/// <para>
/// KNOWN v1 LIMITATIONS (documented; tracked as follow-ups):
/// </para>
/// <list type="number">
/// <item><description><b>Upsert on a database-generated key is unsupported</b> — see
/// <see cref="BuildUpsertSql"/>. An Oracle MERGE joins on the key, which is NULL for a DB-generated
/// key, so it would never match and behave as insert-only. Rather than emit silently-wrong SQL, the
/// builder fails the build with a clear message.</description></item>
/// </list>
/// </summary>
internal sealed class OracleSqlBuilder : SqlBuilder
{
    public override string DialectName => "Oracle";

    /// <summary>
    /// Oracle cannot return multiple result sets from a <c>;</c>-separated batch in a plain
    /// <c>OracleCommand</c> — a second SELECT raises ORA-00933 ("SQL command not properly ended"); the
    /// multi-result shape needs ref cursors / <c>DBMS_SQL.RETURN_RESULT</c>, which the v1 provider does not
    /// emit. So eager loads fall back to the per-relation (multi-round-trip) path instead of the grid path.
    /// </summary>
    public override bool SupportsMultiResultBatch => false;

    /// <summary>
    /// Oracle bind variables use the <c>:name</c> prefix. Oracle identifiers (and so bind names) cannot
    /// begin with <c>_</c>, but the shared generator names synthetic paging parameters <c>__offset</c>
    /// etc. Leading-underscore names move into a generated-safe namespace instead of being trimmed, so
    /// <c>offset</c>, <c>_offset</c>, and <c>__offset</c> remain distinct.
    /// </summary>
    public override string ParameterName(string logicalName) => ":" + SafeBindName(logicalName);

    private static string SafeBindName(string logicalName)
        => !string.IsNullOrEmpty(logicalName) && logicalName[0] == '_'
            ? "inq$" + logicalName.Length.ToString(CultureInfo.InvariantCulture) + "$" + logicalName
            : logicalName;

    /// <summary>
    /// ODP.NET's <c>OracleParameter.set_DbType</c> rejects <c>DbType.DateTime2</c> ("Value does not fall
    /// within the expected range"), so a <see cref="System.DateTime"/> parameter binds
    /// <c>DbType.DateTime</c> — which Oracle accepts and binds to its DATE/TIMESTAMP types — instead of
    /// the <c>DbType.DateTime2</c> the base emits for SqlClient precision. Without this, inserting or
    /// binding any entity with a DateTime column (e.g. Northwind <c>Employee.BirthDate</c>,
    /// <c>Order.OrderDate</c>) throws.
    /// </summary>
    public override string DateTimeDbTypeExpression => "global::System.Data.DbType.DateTime";

    public override string CurrentTimestampExpression => "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    /// <summary>
    /// Unquoted, uppercase-folding identifier policy. Oracle uppercases unquoted identifiers, and the
    /// provider's DDL is written unquoted to match, so valid identifiers are emitted verbatim (no quoting)
    /// to keep the generated SQL aligned with the schema. The exception is an identifier that is not a
    /// legal <i>unquoted</i> Oracle identifier — e.g. one with an embedded space such as <c>Order Details</c>,
    /// which raises ORA-00903 if emitted bare; those are double-quoted (preserving their exact case). This
    /// is the single chokepoint for both DDL and DML, so a quoted name in CREATE TABLE matches every
    /// reference. Reserved words are out of scope (no current identifier collides).
    /// </summary>
    public override string QuoteIdentifier(string identifier)
        => RequiresQuoting(identifier) ? "\"" + identifier.Replace("\"", "\"\"") + "\"" : identifier;

    /// <summary>
    /// True when <paramref name="identifier"/> is not a legal unquoted Oracle identifier and must be
    /// double-quoted: empty, not starting with an ASCII letter, containing a character outside
    /// <c>[A-Za-z0-9_$#]</c>, or an Oracle reserved word.
    /// </summary>
    private static bool RequiresQuoting(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || !IsLetter(identifier[0]))
        {
            return true;
        }

        foreach (var c in identifier)
        {
            if (!IsLetter(c) && (c < '0' || c > '9') && c != '_' && c != '$' && c != '#')
            {
                return true;
            }
        }

        return s_reservedWords.Contains(identifier);
    }

    // Oracle 23c V$RESERVED_WORDS WHERE RESERVED = 'Y' — the keywords that cause ORA-00903 / ORA-01747
    // when used unquoted as an identifier. Only the reserved subset is listed; non-reserved keywords
    // (COMMIT, ROLLBACK, etc.) work unquoted.
    private static readonly HashSet<string> s_reservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCESS", "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC",
        "AUDIT", "BETWEEN", "BY", "CHAR", "CHECK", "CLUSTER", "COLUMN",
        "COLUMN_VALUE", "COMMENT", "COMPRESS", "CONNECT", "CREATE", "CURRENT",
        "DATE", "DECIMAL", "DEFAULT", "DELETE", "DESC", "DISTINCT", "DROP",
        "ELSE", "EXCLUSIVE", "EXISTS", "FILE", "FLOAT", "FOR", "FROM",
        "GRANT", "GROUP", "HAVING", "IDENTIFIED", "IMMEDIATE", "IN",
        "INCREMENT", "INDEX", "INITIAL", "INSERT", "INTEGER", "INTERSECT",
        "INTO", "IS", "LEVEL", "LIKE", "LOCK", "LONG", "MAXEXTENTS",
        "MINUS", "MLSLABEL", "MODE", "MODIFY", "NESTED_TABLE_ID", "NOAUDIT",
        "NOCOMPRESS", "NOT", "NOWAIT", "NULL", "NUMBER", "OF", "OFFLINE",
        "ON", "ONLINE", "OPTION", "OR", "ORDER", "PCTFREE", "PRIOR",
        "PRIVILEGES", "PUBLIC", "RAW", "RENAME", "RESOURCE", "REVOKE", "ROW", "ROWID",
        "ROWNUM", "ROWS", "SELECT", "SESSION", "SET", "SHARE", "SIZE",
        "SMALLINT", "START", "SUCCESSFUL", "SYNONYM", "SYSDATE", "TABLE",
        "THEN", "TO", "TRIGGER", "UID", "UNION", "UNIQUE", "UPDATE",
        "USER", "VALIDATE", "VALUES", "VARCHAR", "VARCHAR2", "VIEW",
        "WHENEVER", "WHERE", "WITH",
    };

    private static bool IsLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    public override string BuildSelectAllSql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + WhereSuffix(context.ActiveRowPredicate);

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<IColumn> filterColumns)
    {
        var where = string.Join(" AND ", filterColumns
            .Select(c => QuoteIdentifier(c.ColumnName) + " = " + ParameterName(c.PropertyName)));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + AppendWhere(where, context.ActiveRowPredicate);
    }

    public override string BuildInsertSql(SqlBuildContext context)
    {
        if (context.InsertableColumns.Count == 0)
        {
            var col = QuoteIdentifier(context.KeyColumns[0].ColumnName);
            return "INSERT INTO " + context.Table + " (" + col + ") VALUES (DEFAULT)";
        }

        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ConcurrencyWhereClause);

    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseMaySupplyKey(context))
        {
            // An Oracle MERGE joins on the key; a DB-generated key is NULL in the source row, so it
            // never matches and the statement degrades to insert-only (and cannot round-trip the
            // generated value). Fail loudly at build time rather than emit silently-wrong SQL. See
            // the type-level "KNOWN v1 LIMITATIONS" note.
            throw new NotSupportedException(GeneratedKeyUpsertUnsupportedMessage);
        }

        var insertColumns = context.InsertColumns;
        var insertParameters = context.InsertParameters;

        return
            "MERGE INTO " + context.Table + " target USING (" + BuildSourceSelect(context) + ") source " +
            "ON (" + BuildSourceJoin(context) + ") " +
            WhenMatchedSet(context) +
            "WHEN NOT MATCHED THEN INSERT (" + insertColumns + ") VALUES (" + insertParameters + ")";
    }

    // A client-key MERGE for an entity with no updatable non-key columns has an empty SET; omit the
    // WHEN MATCHED clause (an Oracle MERGE with only WHEN NOT MATCHED is valid — "insert if absent")
    // instead of the invalid `WHEN MATCHED THEN UPDATE SET ` with an empty body. The generated-key
    // upsert throws NotSupportedException above, so only the client-key path needs this.
    private static string WhenMatchedSet(SqlBuildContext context)
        => context.SetClauses.Length == 0
            ? string.Empty
            : "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " ";

    private const string GeneratedKeyUpsertUnsupportedMessage =
        "Inquiry Oracle provider (v1) does not support upsert on a database-generated key. An Oracle " +
        "MERGE joins on the key, which is NULL for a generated key, so it would never match (insert-" +
        "only) and could not round-trip the generated value. Use a client-supplied key for upsert, or " +
        "split into explicit insert/update.";

    // --- Returning DML (ReturnEntity = true) ----------------------------------------------------
    // Oracle's RETURNING … INTO binds OUT parameters, not a result set, so it cannot feed the reader
    // pipeline directly. Instead each returning op is emitted as an anonymous PL/SQL block that performs
    // the mutation and OPENs a ref cursor (:rc) over the affected row. ExecuteReader on such a block
    // returns that cursor's reader, so the shared QuerySingleOrDefault path materializes it unchanged. The
    // OUT ref-cursor parameter is bound by OracleInquiryConnectionFactory.FinalizeCommand (which detects
    // the PL/SQL block). A database-generated key is captured into a %TYPE-anchored local and re-selected.
    private const string RefCursorBind = ":rc";

    public override string BuildInsertReturningSql(SqlBuildContext context)
    {
        var insert = BuildInsertSql(context);
        if (DatabaseMaySupplyKey(context))
        {
            var keyColumn = context.QuotedKeyColumns[0];
            return
                "DECLARE v_key " + context.Table + "." + keyColumn + "%TYPE; BEGIN " +
                insert + " RETURNING " + keyColumn + " INTO v_key; " +
                "OPEN " + RefCursorBind + " FOR SELECT " + context.SelectColumns + " FROM " + context.Table +
                " WHERE " + keyColumn + " = v_key; END;";
        }

        return
            "BEGIN " + insert + "; OPEN " + RefCursorBind + " FOR SELECT " + context.SelectColumns +
            " FROM " + context.Table + " WHERE " + context.KeyWhereClause + "; END;";
    }

    public override string BuildUpdateReturningSql(SqlBuildContext context)
        // SQL%ROWCOUNT = 0 (no row matched the key + concurrency predicate) opens an empty cursor, so the
        // pipeline returns null — matching result-set RETURNING and letting the concurrency guard fire.
        => "BEGIN " + BuildUpdateSql(context) + "; IF SQL%ROWCOUNT = 0 THEN OPEN " + RefCursorBind +
            " FOR SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE 1 = 0; ELSE OPEN " +
            RefCursorBind + " FOR SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " +
            context.KeyWhereClause + "; END IF; END;";

    public override string BuildUpsertReturningSql(SqlBuildContext context)
        // Reuses the MERGE, which throws for a database-generated key — that case stays unsupported (the
        // MERGE cannot match it). A client-supplied-key upsert always affects its row, so re-select by key.
        => "BEGIN " + BuildUpsertSql(context) + "; OPEN " + RefCursorBind + " FOR SELECT " +
            context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause + "; END;";

    /// <summary>
    /// Oracle 12c+ offset pagination uses the ANSI <c>OFFSET … ROWS FETCH NEXT … ROWS ONLY</c> form
    /// (same as SQL Server), which requires a preceding ORDER BY (enforced in the generator).
    /// </summary>
    public override string BuildPaginationClause(SqlSelectOptions options)
        => "OFFSET " + options.OffsetParameter + " ROWS FETCH NEXT " + options.LimitParameter + " ROWS ONLY";

    /// <summary>
    /// Oracle does not support a row-value <c>(a, b) &gt; (@c0, @c1)</c> comparison, so a multi-column
    /// keyset renders the lexicographic OR-form <c>(a &gt; @c0) OR (a = @c0 AND b &gt; @c1)</c>.
    /// Single-column keysets use the portable scalar form.
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

    private static string BuildSourceSelect(SqlBuildContext context)
        => "SELECT " + string.Join(", ", context.KeyParameters.Select((p, i) => p + " AS k" + i)) + " FROM dual";

    private static string BuildSourceJoin(SqlBuildContext context)
        => string.Join(" AND ", context.QuotedKeyColumns.Select((q, i) => "target." + q + " = source.k" + i));

    // ---- DDL --------------------------------------------------------------------------------

    // Oracle cannot key on CLOB (the unbounded-text fallback); a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    // Oracle's VARCHAR2 caps at 4000 bytes; NVARCHAR2 caps at 2000 characters under the default
    // AL16UTF16 national charset (2 bytes/char × 2000 = 4000 bytes internal limit).
    protected override int MaxBoundedStringLength(bool isUnicode) => isUnicode ? 2000 : 4000;

    // ---- Batch insert (INSERT ALL) -----------------------------------------------------------
    // Oracle has no multi-row VALUES; its set-based multi-row insert is
    //   INSERT ALL INTO t (cols) VALUES (...) INTO t (cols) VALUES (...) SELECT 1 FROM dual
    // — a single INSERT statement, so ExecuteNonQuery still returns the total inserted-row count. The row
    // parameters take the ':' sigil via ParameterName; OracleInquiryConnectionFactory.FinalizeCommand
    // reconciles the binder's '@p{r}_{c}' names by BindByName.
    public override string BuildBatchInsertHeader(SqlBuildContext context) => "INSERT ALL ";

    public override string BuildBatchInsertRowOpen(SqlBuildContext context)
        => "INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (";

    public override string BatchInsertRowSeparator => " ";

    public override string BatchInsertFooter => " SELECT 1 FROM dual";

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean => "NUMBER(1)",
        DbTypeClass.Byte => "NUMBER(3)",
        DbTypeClass.Int16 => "NUMBER(5)",
        DbTypeClass.Int32 => "NUMBER(10)",
        DbTypeClass.Int64 => "NUMBER(19)",
        DbTypeClass.Single => "BINARY_FLOAT",
        DbTypeClass.Double => "BINARY_DOUBLE",
        DbTypeClass.Decimal => "NUMBER(" + DecimalSpec(column, 19, 4) + ")",
        DbTypeClass.DateTime => "TIMESTAMP",
        DbTypeClass.DateTimeOffset => "TIMESTAMP WITH TIME ZONE",
        DbTypeClass.DateOnly => "DATE",
        // Oracle has no time-of-day type. A day-to-second interval bounded to one day (DAY(0)) with
        // SECOND(7) fractional precision preserves TimeOnly's 100ns ticks; ODP.NET maps DbType.Time
        // to OracleDbType.IntervalDS, so parameter binding lines up with this column type.
        DbTypeClass.TimeOnly => "INTERVAL DAY(0) TO SECOND(7)",
        DbTypeClass.Guid => "RAW(16)",
        DbTypeClass.ByteArray => "BLOB",
        // Oracle's VARCHAR2 caps at 4000 bytes; no Length (or one beyond that ceiling) falls back to CLOB
        // rather than emitting the illegal VARCHAR2(>4000).
        _ => column.Length > 0 && column.Length <= MaxBoundedStringLength(column.IsUnicode)
            ? (column.IsUnicode ? "NVARCHAR2(" + column.Length + ")" : "VARCHAR2(" + column.Length + ")")
            : (column.IsUnicode ? "NCLOB" : "CLOB"),
    };

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY";

    // Oracle has no CREATE TABLE IF NOT EXISTS; emit a plain CREATE TABLE (re-create safety is out of scope).
    protected override string WrapCreateTable(SqlBuildContext context, string body)
        => "CREATE TABLE " + context.Table + " (\n    " + body + "\n)";

    // Oracle 12c+ extracts a JSON scalar with JSON_VALUE (returns the value as text).
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "JSON_VALUE(" + quotedColumn + ", '" + jsonPath + "')";

    // Oracle requires a FROM clause; a CASE/EXISTS scalar selects FROM DUAL.
    public override string BuildExistsSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => base.BuildExistsSql(context, predicates) + " FROM DUAL";
}
