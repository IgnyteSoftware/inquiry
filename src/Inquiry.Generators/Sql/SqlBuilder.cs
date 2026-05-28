using Inquiry.Generators.Models;
using System.Collections.Generic;

namespace Inquiry.Generators.Sql;

/// <summary>
/// Compile-time SQL builder consumed by the Inquiry source generator. One concrete subclass exists
/// per supported dialect; the generator resolves the matching subclass for the current
/// compilation's <c>[InquiryDialect]</c> marker and uses it to produce the <c>const string</c>
/// SQL fields baked into generated stores. The Inquiry runtime ships no SQL — every statement is
/// produced here and emitted at compile time.
/// </summary>
internal abstract class SqlBuilder
{
    public abstract string DialectName { get; }

    public virtual string ParameterName(string logicalName) => "@" + logicalName;

    public string QuoteTable(string? schema, string tableName)
    {
        return string.IsNullOrEmpty(schema)
            ? QuoteIdentifier(tableName)
            : QuoteIdentifier(schema!) + "." + QuoteIdentifier(tableName);
    }

    public abstract string QuoteIdentifier(string identifier);

    public abstract string BuildSelectAllSql(SqlBuildContext context);

    public abstract string BuildSelectByKeySql(SqlBuildContext context);

    public abstract string BuildSelectByFieldSql(SqlBuildContext context, IReadOnlyList<ColumnModel> filterColumns);

    public abstract string BuildInsertSql(SqlBuildContext context);

    public abstract string BuildInsertReturningSql(SqlBuildContext context);

    public abstract string BuildUpdateSql(SqlBuildContext context);

    public abstract string BuildUpdateReturningSql(SqlBuildContext context);

    public abstract string BuildDeleteByKeySql(SqlBuildContext context);

    public abstract string BuildUpsertSql(SqlBuildContext context);

    public abstract string BuildUpsertReturningSql(SqlBuildContext context);

    protected static bool DatabaseMaySupplyKey(SqlBuildContext context)
    {
        if (context.KeyColumns.Count != 1) return false;
        var key = context.KeyColumns[0];
        return key.IsGenerated || key.UseDatabaseDefault;
    }
}
