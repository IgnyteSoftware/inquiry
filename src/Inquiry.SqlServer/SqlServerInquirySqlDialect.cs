using Inquiry.Sql;

namespace Inquiry.SqlServer;

/// <summary>
/// Provides SQL Server SQL naming, quoting, and statement generation for Inquiry.
/// </summary>
public sealed class SqlServerInquirySqlDialect : InquirySqlDialect
{
    /// <inheritdoc />
    public override string Name => "SqlServer";

    /// <inheritdoc />
    public override string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier cannot be empty.", nameof(identifier));
        }

        return "[" + identifier.Replace("]", "]]") + "]";
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
        return "INSERT INTO " + context.Table
            + " (" + context.InsertColumns + ") OUTPUT " + InsertedColumns(context)
            + " VALUES (" + context.InsertParameters + ")";
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
        return "UPDATE " + context.Table + " SET " + context.SetClauses
            + " OUTPUT " + InsertedColumns(context)
            + " WHERE " + context.QuotedKeyColumn + " = " + context.KeyParameter;
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
    /// Uses a <c>MERGE</c> statement to atomically insert or update a single row.
    /// </remarks>
    public override string BuildUpsertSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        return
            $"MERGE INTO {context.Table} AS target " +
            $"USING (SELECT {context.KeyParameter} AS k) AS source ON target.{context.QuotedKeyColumn} = source.k " +
            $"WHEN MATCHED THEN UPDATE SET {context.SetClauses} " +
            $"WHEN NOT MATCHED THEN INSERT ({context.InsertColumns}) VALUES ({context.InsertParameters});";
    }

    /// <inheritdoc />
    public override string BuildUpsertReturningSql(InquirySqlBuildContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        EnsureCanUpsert(context);
        return
            $"MERGE INTO {context.Table} AS target " +
            $"USING (SELECT {context.KeyParameter} AS k) AS source ON target.{context.QuotedKeyColumn} = source.k " +
            $"WHEN MATCHED THEN UPDATE SET {context.SetClauses} " +
            $"WHEN NOT MATCHED THEN INSERT ({context.InsertColumns}) VALUES ({context.InsertParameters}) " +
            $"OUTPUT {InsertedColumns(context)};";
    }

    private string InsertedColumns(InquirySqlBuildContext context)
        => string.Join(", ", context.Columns.Select(c => "INSERTED." + QuoteIdentifier(c.ColumnName)));
}
