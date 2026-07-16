using Inquiry.Generators.Abstractions;
using Inquiry.Oracle.Shared;
using System;
using System.Collections.Generic;
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
    public override bool UsesArrayBindingForBatchMutations => true;

    public override string BuildArrayBindCountAssignment(string commandExpression, string countExpression)
        => $"((global::Oracle.ManagedDataAccess.Client.OracleCommand){commandExpression}).ArrayBindCount = {countExpression};";

    public override string? BuildArrayBindSizeExpression(string valueExpression, string valueVariable, IColumn column)
        => column.TypeClass switch
        {
            DbTypeClass.String => $"{valueExpression} is string {valueVariable} ? {valueVariable}.Length : 0",
            DbTypeClass.ByteArray => $"{valueExpression} is byte[] {valueVariable} ? {valueVariable}.Length : 0",
            _ => null,
        };

    public override string BuildArrayBindSizeAssignment(string parameterExpression, string sizesExpression)
        => $"((global::Oracle.ManagedDataAccess.Client.OracleParameter){parameterExpression}).ArrayBindSize = {sizesExpression};";

    public override string? BuildArrayBindParameterMetadata(string parameterExpression, IColumn column)
        => column.TypeClass == DbTypeClass.TimeOnly
            ? $"((global::Oracle.ManagedDataAccess.Client.OracleParameter){parameterExpression}).OracleDbType = global::Oracle.ManagedDataAccess.Client.OracleDbType.IntervalDS;"
            : null;

    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison CheckConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison ForeignKeyConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override string DialectName => "Oracle";
    public override string ProviderId => "oracle";
    // Oracle requires DEFAULT before inline constraints such as NOT NULL.
    protected override bool DefaultExpressionPrecedesInlineConstraints => true;
    // Legal unquoted names fold to uppercase; names which require quotes retain exact identity.
    public override string GetPhysicalIdentifierSortKey(string identifier)
        => RequiresQuoting(identifier) ? "1\0" + identifier : "0\0" + FoldAscii(identifier, upper: true);

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.AlterTable;
    public override bool SupportsCheckConstraints => true;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent @event)
        => @event == ReferentialActionEvent.Update ? action == ReferentialActionKind.NoAction : action is ReferentialActionKind.NoAction or ReferentialActionKind.Cascade or ReferentialActionKind.SetNull;

    public override string BuildReaderExpression(ReaderExpressionContext context)
    {
        if (context.ProviderIsDateOnly)
        {
            return $"global::System.DateOnly.FromDateTime(reader.GetDateTime({context.Ordinal}))";
        }

        if (context.ProviderIsTimeOnly)
        {
            return $"global::System.TimeOnly.FromTimeSpan(reader.GetFieldValue<global::System.TimeSpan>({context.Ordinal}))";
        }

        return base.BuildReaderExpression(context);
    }

    /// <summary>
    /// Oracle cannot return multiple result sets from a <c>;</c>-separated batch in a plain
    /// <c>OracleCommand</c> — a second SELECT raises ORA-00933 ("SQL command not properly ended") — so the
    /// eager-load grid command is wrapped in an anonymous PL/SQL block that OPENs each SELECT into a ref
    /// cursor and hands it to the client with <c>DBMS_SQL.RETURN_RESULT</c> (12c+ implicit result sets).
    /// ODP.NET surfaces implicit results through the ordinary <c>ExecuteReader</c>/<c>NextResult</c>
    /// protocol, so the shared grid reader consumes them unchanged. Reusing the single cursor variable is
    /// legal — <c>RETURN_RESULT</c> transfers ownership of the open cursor to the client. The block never
    /// references <c>:rc</c>, so <c>OracleInquiryConnectionFactory.FinalizeCommand</c>'s returning-block
    /// detection does not bind a stray OUT ref cursor onto it.
    /// </summary>
    public override string MultiResultBatchPrefix => "DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR ";

    /// <inheritdoc cref="MultiResultBatchPrefix"/>
    public override string MultiResultBatchSeparator => "; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR ";

    /// <inheritdoc cref="MultiResultBatchPrefix"/>
    public override string MultiResultBatchSuffix => "; DBMS_SQL.RETURN_RESULT(c); END;";

    /// <summary>
    /// Oracle bind variables use the <c>:name</c> prefix. Oracle identifiers (and so bind names) cannot
    /// begin with <c>_</c>, but the shared generator names synthetic paging parameters <c>__offset</c>
    /// etc. Leading-underscore names move into a generated-safe namespace instead of being trimmed, so
    /// <c>offset</c>, <c>_offset</c>, and <c>__offset</c> remain distinct.
    /// </summary>
    public override string ParameterName(string logicalName) => ":" + OracleBindName.Encode(logicalName);
    public override string RuntimeParameterName(string logicalName) => OracleBindName.Encode(logicalName);
    public override string RuntimeParameterNameFromSql(string sqlParameterName)
        => sqlParameterName.Length > 0 && (sqlParameterName[0] == ':' || sqlParameterName[0] == '@')
            ? sqlParameterName.Substring(1)
            : sqlParameterName;
    public override string StoredProcedureParameterName(string formalName)
        => formalName.Length > 0 && formalName[0] is '@' or ':' or '$' or '?'
            ? formalName.Substring(1)
            : formalName;

    /// <summary>
    /// Oracle stored procedures cannot return result sets directly; the caller must pass an
    /// <c>OUT SYS_REFCURSOR</c> parameter. Rather than requiring users to declare cursor parameters
    /// in C#, the generator wraps entity-returning procedure calls in a PL/SQL block that declares
    /// local cursor variables, passes them to the procedure, and surfaces each one through
    /// <c>DBMS_SQL.RETURN_RESULT</c> (12c+ implicit result sets). ODP.NET exposes implicit results
    /// through <c>ExecuteReader</c>/<c>NextResult</c>, so the shared reader pipeline consumes them
    /// unchanged. The block never references <c>:rc</c>, so <c>FinalizeCommand</c>'s returning-block
    /// detection does not fire.
    /// </summary>
    public override string? BuildProcedureReaderCall(string procedureName, IReadOnlyList<string> parameterNames, int resultSetCount)
    {
        var sb = new System.Text.StringBuilder("DECLARE ");
        for (var i = 0; i < resultSetCount; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append('c').Append(i).Append(" SYS_REFCURSOR;");
        }
        sb.Append(" BEGIN ").Append(procedureName).Append('(');

        var argIndex = 0;
        for (var i = 0; i < parameterNames.Count; i++)
        {
            if (argIndex++ > 0) sb.Append(", ");
            sb.Append(':').Append(OracleBindName.Encode(parameterNames[i]));
        }
        for (var i = 0; i < resultSetCount; i++)
        {
            if (argIndex++ > 0) sb.Append(", ");
            sb.Append('c').Append(i);
        }
        sb.Append("); ");

        for (var i = 0; i < resultSetCount; i++)
        {
            sb.Append("DBMS_SQL.RETURN_RESULT(c").Append(i).Append("); ");
        }
        sb.Append("END;");
        return sb.ToString();
    }

    /// <summary>
    /// ODP.NET's <c>OracleParameter.set_DbType</c> rejects <c>DbType.DateTime2</c> ("Value does not fall
    /// within the expected range"), so a <see cref="System.DateTime"/> parameter binds
    /// <c>DbType.DateTime</c> — which Oracle accepts and binds to its DATE/TIMESTAMP types — instead of
    /// the <c>DbType.DateTime2</c> the base emits for SqlClient precision. Without this, inserting or
    /// binding any entity with a DateTime column (e.g. Northwind <c>Employee.BirthDate</c>,
    /// <c>Order.OrderDate</c>) throws.
    /// </summary>
    public override string DateTimeDbTypeExpression => "global::System.Data.DbType.DateTime";

    // ODP.NET accepts the original CLR values when their metadata matches RAW(16)/NUMBER(1).
    public override string GuidDbTypeExpression => "global::System.Data.DbType.Binary";
    public override string BooleanDbTypeExpression => "global::System.Data.DbType.Int32";
    public override string? TimeOnlyDbTypeExpression => null;

    public override string BuildParameterValueExpression(ParameterValueExpressionContext context)
    {
        if (context.ProviderIsDateOnly)
        {
            return $"{context.ValueExpression}.ToDateTime(global::System.TimeOnly.MinValue)";
        }

        if (context.ProviderIsTimeOnly)
        {
            return $"{context.ValueExpression}.ToTimeSpan()";
        }

        return base.BuildParameterValueExpression(context);
    }

    public override string BuildParameterValueTypeName(ParameterValueExpressionContext context)
    {
        if (context.ProviderIsDateOnly)
        {
            return "global::System.DateTime";
        }

        if (context.ProviderIsTimeOnly)
        {
            return "global::System.TimeSpan";
        }

        return base.BuildParameterValueTypeName(context);
    }

    public override string CurrentTimestampExpression => "SYS_EXTRACT_UTC(SYSTIMESTAMP)";

    /// <summary>For <c>TIMESTAMP WITH TIME ZONE</c> columns, retain the timezone-aware form.</summary>
    public override string CurrentTimestampOffsetExpression => "SYSTIMESTAMP AT TIME ZONE 'UTC'";

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

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

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

    /// <remarks>
    /// Oracle MERGE is not serializable against concurrent inserts: a second session can insert
    /// between the NOT MATCHED evaluation and the INSERT, raising ORA-00001 (unique constraint
    /// violation). Callers that expect concurrent upserts on the same key should catch
    /// <c>OracleException</c> with <c>Number == 1</c> and retry.
    /// </remarks>
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

    protected override string TopOneSuffix => "OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

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
    public override bool RequiresBoundedComputedStrings => true;

    protected override string RenderComputedColumn(IColumn column)
    {
        if (column.TypeClass != DbTypeClass.String)
            return base.RenderComputedColumn(column);

        var type = column.IsUnicode ? "NVARCHAR2" : "VARCHAR2";
        return "AS (CAST(" + column.ComputedExpression + " AS " + type + "(" +
            column.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")))";
    }

    // ---- Batch insert (single-table INSERT SELECT) -------------------------------------------
    // Oracle has no multi-row VALUES. A multitable INSERT ALL evaluates a sequence only once for
    // the source row, so it cannot safely populate several identity rows. Emit one single-table
    // INSERT fed by SELECT ... FROM dual UNION ALL instead; each SELECT is an independent source row.
    public override string BuildBatchInsertHeader(SqlBuildContext context)
        => "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") ";

    public override string BuildBatchInsertRowOpen(SqlBuildContext context)
        => "SELECT ";

    public override string BatchInsertRowClose => " FROM dual";
    public override string BatchInsertRowSeparator => " UNION ALL ";
    public override string BatchInsertSqlParameterPrefix => ":iq1$b";
    public override string BatchInsertRuntimeParameterPrefix => "iq1$b";

    public override string BatchInsertFooter => string.Empty;

    protected override string MapColumnType(IColumn column) => column.TypeClass switch
    {
        DbTypeClass.Boolean => "NUMBER(1)",
        DbTypeClass.Byte => "NUMBER(3)",
        DbTypeClass.Int16 => "NUMBER(5)",
        DbTypeClass.Int32 => "NUMBER(10)",
        DbTypeClass.Int64 => "NUMBER(19)",
        DbTypeClass.Single => "BINARY_FLOAT",
        DbTypeClass.Double => "BINARY_DOUBLE",
        DbTypeClass.Decimal => "NUMBER(" + DecimalSpec(column, 18, 2) + ")",
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

    // Oracle has no CREATE TABLE IF NOT EXISTS; the generated DDL is a single const string executed
    // as one command, and multiple PL/SQL anonymous blocks cannot share a single command, so an
    // EXECUTE IMMEDIATE wrapper is not viable here. Consumers that need idempotency should wrap each
    // statement in their migration runner: BEGIN EXECUTE IMMEDIATE '...'; EXCEPTION WHEN OTHERS THEN
    // IF SQLCODE != -955 THEN RAISE; END IF; END;
    protected override string WrapCreateTable(SqlBuildContext context, string body)
        => "CREATE TABLE " + context.Table + " (\n    " + body + "\n)";

    public override bool UseArrayInParameters => true;

    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
    {
        var (colType, selectExpr) = elementType switch
        {
            // ODP.NET stores a CLR Guid in .NET's mixed-endian byte layout: reverse the byte order of
            // the first 4/2/2-byte fields from the canonical JSON string, leaving the final 8 bytes as-is.
            DbTypeClass.Guid => ("VARCHAR2(36)", "HEXTORAW(SUBSTR(jt.val, 7, 2) || SUBSTR(jt.val, 5, 2) || SUBSTR(jt.val, 3, 2) || SUBSTR(jt.val, 1, 2) || SUBSTR(jt.val, 12, 2) || SUBSTR(jt.val, 10, 2) || SUBSTR(jt.val, 17, 2) || SUBSTR(jt.val, 15, 2) || SUBSTR(jt.val, 20, 4) || SUBSTR(jt.val, 25, 12))"),
            // Project JSON true/false as text, then map it explicitly. This works on the advertised
            // Oracle 12c+ range; the newer ALLOW BOOLEAN TO NUMBER CONVERSION clause is not portable
            // across that full range.
            DbTypeClass.Boolean => ("VARCHAR2(5)", "CASE jt.val WHEN 'true' THEN 1 WHEN 'false' THEN 0 END"),
            DbTypeClass.Byte or DbTypeClass.Int16 or DbTypeClass.Int32 => ("NUMBER(10)", "jt.val"),
            DbTypeClass.Int64 => ("NUMBER(19)", "jt.val"),
            DbTypeClass.Single => ("BINARY_FLOAT", "jt.val"),
            DbTypeClass.Double => ("BINARY_DOUBLE", "jt.val"),
            DbTypeClass.Decimal => ("NUMBER", "jt.val"),
            _ => ("VARCHAR2(4000)", "jt.val"),
        };

        return quotedColumn + " IN (SELECT " + selectExpr + " FROM JSON_TABLE(" + parameterName
            + ", '$[*]' COLUMNS(val " + colType + " PATH '$')) jt)";
    }

    public override string ArrayParameterBinderFqn => "global::Inquiry.Parameters.InquiryJsonArrayParameter";

    // Oracle 12c+ extracts a JSON scalar with JSON_VALUE (returns the value as text).
    protected override string RenderJsonPathExtract(string quotedColumn, string jsonPath)
        => "JSON_VALUE(" + quotedColumn + ", '" + jsonPath + "')";

    // Oracle requires a FROM clause; a CASE/EXISTS scalar selects FROM DUAL.
    public override string BuildExistsSql(SqlBuildContext context, IReadOnlyList<SqlPredicate> predicates)
        => base.BuildExistsSql(context, predicates) + " FROM DUAL";

    protected override string BuildLockSuffix(int lockMode) => lockMode switch
    {
        1 => " FOR UPDATE",
        2 => " FOR UPDATE NOWAIT",
        3 => " FOR UPDATE SKIP LOCKED",
        4 => throw new System.NotSupportedException("Oracle does not support FOR SHARE locking."),
        _ => "",
    };
}
