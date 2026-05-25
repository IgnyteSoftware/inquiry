using Inquiry.Sql;

namespace Inquiry.PostgreSql;

/// <summary>
/// Provides PostgreSQL SQL naming, quoting, and statement generation for Inquiry.
/// </summary>
public sealed class PostgreSqlInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "PostgreSql";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    /// <inheritdoc />
    public override string BuildSelectAllSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table;
    }

    /// <inheritdoc />
    public override string BuildSelectByKeySql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + context.QuotedKeyColumn + " = " + ParameterName("key");
    }

    /// <inheritdoc />
    public override string BuildSelectByFieldSql(InquirySqlBuildContext context, InquirySqlColumn column)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (column is null) throw new ArgumentNullException(nameof(column));
        return "SELECT " + context.SelectColumns + " FROM " + context.Table
            + " WHERE " + QuoteIdentifier(column.ColumnName) + " = " + ParameterName("value");
    }

    /// <inheritdoc />
    public override string BuildInsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanInsert(context);
        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";
    }

    /// <inheritdoc />
    public override string BuildInsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanInsert(context);
        return BuildInsertSql(context) + " RETURNING " + context.SelectColumns;
    }

    /// <inheritdoc />
    public override string BuildUpdateSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpdate(context);
        return "UPDATE " + context.Table + " SET " + context.SetClauses
            + " WHERE " + context.QuotedKeyColumn + " = " + context.KeyParameter;
    }

    /// <inheritdoc />
    public override string BuildUpdateReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpdate(context);
        return BuildUpdateSql(context) + " RETURNING " + context.SelectColumns;
    }

    /// <inheritdoc />
    public override string BuildDeleteByKeySql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return "DELETE FROM " + context.Table
            + " WHERE " + context.QuotedKeyColumn + " = " + ParameterName("key");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses PostgreSQL 9.5+ <c>INSERT ... ON CONFLICT DO UPDATE</c> syntax.
    /// </remarks>
    public override string BuildUpsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        return $"INSERT INTO {context.Table} ({context.InsertColumns}) VALUES ({context.InsertParameters}) " +
               $"ON CONFLICT ({context.QuotedKeyColumn}) DO UPDATE SET {context.SetClauses}";
    }

    /// <inheritdoc />
    public override string BuildUpsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        return BuildUpsertSql(context) + " RETURNING " + context.SelectColumns;
    }
}
