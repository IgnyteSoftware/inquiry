using System;

namespace Inquiry.Generators.Sql;

internal sealed class SqlStatementSet
{
    private readonly Func<SqlColumn, string> _selectByField;

    public SqlStatementSet(
        string selectAll,
        string selectByKey,
        string deleteByKey,
        string insert,
        string update,
        Func<SqlColumn, string> selectByField)
    {
        SelectAll = selectAll ?? throw new ArgumentNullException(nameof(selectAll));
        SelectByKey = selectByKey ?? throw new ArgumentNullException(nameof(selectByKey));
        DeleteByKey = deleteByKey ?? throw new ArgumentNullException(nameof(deleteByKey));
        Insert = insert ?? throw new ArgumentNullException(nameof(insert));
        Update = update ?? throw new ArgumentNullException(nameof(update));
        _selectByField = selectByField ?? throw new ArgumentNullException(nameof(selectByField));
    }

    public string SelectAll { get; }

    public string SelectByKey { get; }

    public string DeleteByKey { get; }

    public string Insert { get; }

    public string Update { get; }

    public string SelectByField(SqlColumn fieldColumn)
    {
        return _selectByField(fieldColumn);
    }
}
