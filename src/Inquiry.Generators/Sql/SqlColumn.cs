using System;

namespace Inquiry.Generators.Sql;

internal sealed class SqlColumn
{
    public SqlColumn(string propertyName, string columnName, bool isKey)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(columnName));
        }

        PropertyName = propertyName;
        ColumnName = columnName;
        IsKey = isKey;
    }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public bool IsKey { get; }
}
