using System;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.Generators.Sql;

internal sealed class SqlStatementBuilder
{
    private readonly InquirySqlDialect _dialect;

    public SqlStatementBuilder(InquirySqlDialect dialect)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
    }

    public SqlStatementSet Build(string? schema, string tableName, IReadOnlyList<SqlColumn> columns)
    {
        if (columns is null)
        {
            throw new ArgumentNullException(nameof(columns));
        }

        var key = columns.Single(c => c.IsKey);
        var table = _dialect.QuoteTable(schema, tableName);
        var selectColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertColumns = string.Join(", ", columns.Select(c => _dialect.QuoteIdentifier(c.ColumnName)));
        var insertParameters = string.Join(", ", columns.Select(c => _dialect.ParameterName(c.PropertyName)));
        var setClauses = string.Join(", ", columns
            .Where(c => !c.IsKey)
            .Select(c => _dialect.QuoteIdentifier(c.ColumnName) + " = " + _dialect.ParameterName(c.PropertyName)));

        return new SqlStatementSet(
            selectAll: "SELECT " + selectColumns + " FROM " + table,
            selectByKey: "SELECT " + selectColumns + " FROM " + table + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName("key"),
            deleteByKey: "DELETE FROM " + table + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName("key"),
            insert: "INSERT INTO " + table + " (" + insertColumns + ") VALUES (" + insertParameters + ")",
            update: "UPDATE " + table + " SET " + setClauses + " WHERE " + _dialect.QuoteIdentifier(key.ColumnName) + " = " + _dialect.ParameterName(key.PropertyName),
            selectByField: fieldColumn => "SELECT " + selectColumns + " FROM " + table + " WHERE " + _dialect.QuoteIdentifier(fieldColumn.ColumnName) + " = " + _dialect.ParameterName("value"));
    }
}
