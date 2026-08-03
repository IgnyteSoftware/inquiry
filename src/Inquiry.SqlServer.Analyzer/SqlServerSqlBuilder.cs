using Inquiry.Generators.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Inquiry.SqlServer.Analyzer;

internal sealed class SqlServerSqlBuilder : SqlBuilder
{
    public override BatchInsertStrategy BatchInsertStrategy => BatchInsertStrategy.Adaptive;
    public override int BatchInsertAdaptiveThreshold => 250;

    // SQL Server rejects a VALUES table-value constructor with more than 1,000 rows.
    public override int BatchInsertMaxRowsPerCommand => 1000;

    // Documented SQL Server stored-procedure/command parameter ceiling.
    public override int HardMaxParametersPerCommand => 2100;

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
    public override string ProviderId => "sqlserver";
    // Stable conservative envelope for the commonly case-insensitive SQL Server collations.
    public override string GetPhysicalIdentifierSortKey(string identifier) => FoldAscii(identifier, upper: true);
    public override string GetProviderArtifactKind(CollectionParameterArtifact artifact) => "tvp";

    public override CyclicForeignKeyStrategy CyclicForeignKeyStrategy => CyclicForeignKeyStrategy.AlterTable;
    public override bool SupportsIndexIncludeColumns => true;
    public override bool SupportsCheckConstraints => true;
    public override ConstraintNameScope IndexNameScope => ConstraintNameScope.Table;
    public override IdentifierComparison IndexNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison CheckConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override IdentifierComparison ForeignKeyConstraintNameComparison => IdentifierComparison.OrdinalIgnoreCase;
    public override bool SupportsReferentialAction(ReferentialActionKind action, ReferentialActionEvent @event) => action is ReferentialActionKind.NoAction or ReferentialActionKind.Cascade or ReferentialActionKind.SetNull or ReferentialActionKind.SetDefault;

    public override bool SupportsDatabaseGeneratedConcurrencyToken => true;

    public override string SequentialGuidFactoryExpression => "global::Inquiry.InquiryGuid.NewSqlServerSequential()";

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

    /// <inheritdoc/>
    public override bool CollectionBindingBypassesBatchSizeLimit => true;

    /// <inheritdoc cref="UseArrayInParameters"/>
    protected override string RenderIn(string quotedColumn, string parameterName, DbTypeClass elementType)
        => quotedColumn + " IN (SELECT [Value] FROM " + parameterName + ")";

    /// <inheritdoc />
    public override string ArrayParameterBinderFqn => "global::Inquiry.SqlServer.Parameters.InquiryTvpParameter";

    public override CollectionParameterResolution ResolveCollectionParameter(CollectionParameterContext context)
        => SqlServerTvpResolver.Resolve(this, context);

    public override string BuildCollectionParameterBinding(CollectionParameterBindingContext context)
    {
        var artifact = context.Resolution.Artifact
            ?? throw new System.InvalidOperationException("A successful SQL Server TVP resolution must include an artifact.");
        return $"global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind({context.CommandExpression}, \"{context.ParameterName}\", {context.ValueExpression}, \"{EscapeLiteral(artifact.RuntimeTypeName)}\", {artifact.RuntimeDescriptorFieldName});";
    }

    public override ProcedureTvpResolution? ResolveProcedureTvp(ProcedureTvpContext context)
        => SqlServerTvpResolver.ResolveProcedure(context);

    private static string EscapeLiteral(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
            + " WHERE " + context.KeyWriteWhereClause;

    // OUTPUT emits only the rows the UPDATE actually touched, so composing the enforced predicate onto
    // the WHERE is sufficient — the trailing SELECT reads @_out, not the table.
    public override string BuildUpdateReturningSql(SqlBuildContext context)
        => DeclareOutputTable(context)
            + " UPDATE " + context.Table + " SET " + context.SetClausesWithVersion
            + " OUTPUT " + InsertedColumns(context) + " INTO @_out"
            + " WHERE " + context.KeyWriteWhereClause
            + "; " + SelectFromOutput(context);

    public override string BuildDeleteByKeySql(SqlBuildContext context)
        => "DELETE FROM " + context.Table + " WHERE " + context.KeyWriteWhereClause;

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
            QuoteIdentifier(c.ColumnName) + " " + (c.IsDatabaseGeneratedToken ? "BINARY(8)" : MapColumnType(c)))) + ");";

    private string SelectFromOutput(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM @_out";

    private string BuildGeneratedKeyUpsertSql(SqlBuildContext context, bool returning)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyParameter = context.KeyParameters[0];
        var output = returning
            ? " OUTPUT " + InsertedColumns(context) + " INTO @_out (" + context.SelectColumns + ")"
            : string.Empty;

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
            elseBranch = BuildGeneratedKeyEmptySetBranch(
                context,
                returning,
                keyColumn,
                keyParameter,
                explicitInsertCols,
                explicitInsertParams,
                output,
                isIdentity);
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
            (context.SetClauses.Length == 0 ? string.Empty : identityOn) +
            elseBranch +
            (context.SetClauses.Length == 0 ? string.Empty : identityOff) +
            "END" +
            trailing;
    }

    private string BuildGeneratedKeyEmptySetBranch(
        SqlBuildContext context,
        bool returning,
        string keyColumn,
        string keyParameter,
        string explicitInsertColumns,
        string explicitInsertParameters,
        string output,
        bool isIdentity)
    {
        var identityOn = isIdentity
            ? "SET IDENTITY_INSERT " + context.Table + " ON; SET @_inquiry_identity_insert = 1; "
            : string.Empty;
        var identityOff = isIdentity
            ? "SET IDENTITY_INSERT " + context.Table + " OFF; SET @_inquiry_identity_insert = 0; "
            : string.Empty;
        var identityCleanup = isIdentity
            ? "IF @_inquiry_identity_insert = 1 " +
              "BEGIN TRY SET IDENTITY_INSERT " + context.Table + " OFF; SET @_inquiry_identity_insert = 0; END TRY BEGIN CATCH END CATCH; "
            : string.Empty;
        var insert =
            identityOn +
            "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ")" + output +
            " VALUES (" + explicitInsertParameters + "); " +
            identityOff;

        var lockAndInsert = returning
            ? "INSERT INTO @_out (" + context.SelectColumns + ") " +
              "SELECT " + context.SelectColumns + " FROM " + context.Table +
              " WITH (UPDLOCK, SERIALIZABLE) WHERE " + keyColumn + " = " + keyParameter + "; " +
              "IF @@ROWCOUNT = 0 BEGIN " + insert + "END; "
            : "IF NOT EXISTS (SELECT 1 FROM " + context.Table + " WITH (UPDLOCK, SERIALIZABLE) WHERE " +
              keyColumn + " = " + keyParameter + ") BEGIN " + insert + "END; ";

        return
            "DECLARE @_inquiry_started_transaction bit = 0; " +
            "DECLARE @_inquiry_identity_insert bit = 0; " +
            "DECLARE @_inquiry_savepoint_created bit = 0; " +
            "DECLARE @_inquiry_savepoint nvarchar(32) = N'InquiryUpsert_' + RIGHT(REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 16); " +
            "BEGIN TRY " +
            "IF @@TRANCOUNT = 0 BEGIN BEGIN TRANSACTION; SET @_inquiry_started_transaction = 1; END " +
            "ELSE BEGIN SAVE TRANSACTION @_inquiry_savepoint; SET @_inquiry_savepoint_created = 1; END; " +
            lockAndInsert +
            "IF @_inquiry_started_transaction = 1 COMMIT TRANSACTION; " +
            "END TRY " +
            "BEGIN CATCH " +
            identityCleanup +
            "IF @_inquiry_started_transaction = 1 BEGIN IF XACT_STATE() <> 0 ROLLBACK TRANSACTION; END " +
            "ELSE IF @_inquiry_savepoint_created = 1 AND XACT_STATE() = 1 ROLLBACK TRANSACTION @_inquiry_savepoint; " +
            "THROW; " +
            "END CATCH; ";
    }

    private static string JoinSql(string first, string rest)
        => string.IsNullOrEmpty(rest) ? first : first + ", " + rest;

    // ---- DDL --------------------------------------------------------------------------------

    // SQL Server cannot key on NVARCHAR(MAX); a string key needs an explicit Length.
    public override bool RequiresBoundedStringKeys => true;

    // nvarchar tops out at 4000 chars, varchar at 8000; a longer declared Length maps to NVARCHAR(MAX) /
    // VARCHAR(MAX), which cannot be keyed or indexed (see MapColumnType).
    protected override int MaxBoundedStringLength(bool isUnicode) => isUnicode ? 4000 : 8000;

    protected override string MapColumnType(IColumn column)
        => SqlServerTvpResolver.InferredColumnDdl(column);

        // A declared Length beyond the fixed-width ceiling (nvarchar 4000 / varchar 8000) is not a legal
        // bounded type — NVARCHAR(5000) is a DDL error — so it maps to the MAX type instead of emitting
        // invalid SQL. For a regular column that yields valid DDL; for a string KEY or indexed column the
        // MAX type cannot be keyed/indexed, which INQ031/INQ032 now report (the over-ceiling case is folded
        // into MapsToUnboundedString via MaxBoundedStringLength).

    protected override string ColumnType(IColumn column)
        => column.IsDatabaseGeneratedToken ? "ROWVERSION" : base.ColumnType(column);

    protected override string GeneratedKeyClause(IColumn column)
        => MapColumnType(column) + " IDENTITY(1,1) PRIMARY KEY";

    // The shared feature catalog uses the ANSI concatenation operator in computed expressions.
    // SQL Server spells string concatenation with +.
    public override string RenderComputedExpression(string expression)
        => SqlExpressionLexer.Analyze(expression, SqlExpressionCommentPolicy.Standard, true).RenderedExpression;

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

    public override string ApplyLockClause(string selectSql, SqlBuildContext context, int lockMode)
    {
        if (lockMode == 0) return selectSql;
        var hint = lockMode switch
        {
            1 => " WITH (UPDLOCK, ROWLOCK)",
            2 => " WITH (UPDLOCK, ROWLOCK, NOWAIT)",
            3 => " WITH (UPDLOCK, ROWLOCK, READPAST)",
            4 => " WITH (HOLDLOCK, ROWLOCK)",
            _ => "",
        };
        var idx = selectSql.IndexOf(context.Table, System.StringComparison.Ordinal);
        if (idx >= 0)
        {
            return selectSql.Insert(idx + context.Table.Length, hint);
        }
        return selectSql;
    }
}
