namespace Inquiry;

/// <summary>
/// Contains generated SQL statements for a mapped Inquiry entity.
/// </summary>
public sealed class InquirySqlStatementSet
{
    private readonly Func<InquirySqlColumn, string> _selectByField;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlStatementSet"/> class.
    /// </summary>
    public InquirySqlStatementSet(
        string selectAll,
        string selectByKey,
        string deleteByKey,
        string insert,
        string update,
        Func<InquirySqlColumn, string> selectByField)
    {
        SelectAll = selectAll ?? throw new ArgumentNullException(nameof(selectAll));
        SelectByKey = selectByKey ?? throw new ArgumentNullException(nameof(selectByKey));
        DeleteByKey = deleteByKey ?? throw new ArgumentNullException(nameof(deleteByKey));
        Insert = insert ?? throw new ArgumentNullException(nameof(insert));
        Update = update ?? throw new ArgumentNullException(nameof(update));
        _selectByField = selectByField ?? throw new ArgumentNullException(nameof(selectByField));
    }

    /// <summary>
    /// Gets the statement used to select all rows.
    /// </summary>
    public string SelectAll { get; }

    /// <summary>
    /// Gets the statement used to select one row by key.
    /// </summary>
    public string SelectByKey { get; }

    /// <summary>
    /// Gets the statement used to delete one row by key.
    /// </summary>
    public string DeleteByKey { get; }

    /// <summary>
    /// Gets the statement used to insert one row.
    /// </summary>
    public string Insert { get; }

    /// <summary>
    /// Gets the statement used to update one row.
    /// </summary>
    public string Update { get; }

    /// <summary>
    /// Builds the statement used to select rows by a field.
    /// </summary>
    public string SelectByField(InquirySqlColumn fieldColumn)
    {
        return _selectByField(fieldColumn);
    }
}
