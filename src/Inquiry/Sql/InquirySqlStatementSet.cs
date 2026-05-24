namespace Inquiry.Sql;

/// <summary>
/// Contains generated SQL statements for a mapped Inquiry entity.
/// </summary>
public sealed class InquirySqlStatementSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquirySqlStatementSet"/> class.
    /// </summary>
    public InquirySqlStatementSet(
        string selectAll,
        string selectByKey,
        string deleteByKey,
        string insert,
        string update,
        IReadOnlyDictionary<string, string> selectByField)
    {
        SelectAll = selectAll ?? throw new ArgumentNullException(nameof(selectAll));
        SelectByKey = selectByKey ?? throw new ArgumentNullException(nameof(selectByKey));
        DeleteByKey = deleteByKey ?? throw new ArgumentNullException(nameof(deleteByKey));
        Insert = insert ?? throw new ArgumentNullException(nameof(insert));
        Update = update ?? throw new ArgumentNullException(nameof(update));
        SelectByField = selectByField ?? throw new ArgumentNullException(nameof(selectByField));
    }

    /// <summary>Gets the statement used to select all rows.</summary>
    public string SelectAll { get; }

    /// <summary>Gets the statement used to select one row by key.</summary>
    public string SelectByKey { get; }

    /// <summary>Gets the statement used to delete one row by key.</summary>
    public string DeleteByKey { get; }

    /// <summary>Gets the statement used to insert one row.</summary>
    public string Insert { get; }

    /// <summary>Gets the statement used to update one row.</summary>
    public string Update { get; }

    /// <summary>
    /// Gets statements that select all rows by a mapped field, keyed by the entity property name.
    /// </summary>
    public IReadOnlyDictionary<string, string> SelectByField { get; }
}
