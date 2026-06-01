namespace Inquiry.Stores;

/// <summary>
/// Generates a full-text search over one or more mapped columns. The method takes a single
/// <see cref="string"/> search-term parameter (plus the trailing
/// <see cref="System.Threading.CancellationToken"/>) and returns the matching entities
/// (<c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// Supported on PostgreSQL (<c>to_tsvector @@ plainto_tsquery</c>), SQL Server (<c>FREETEXT</c>), and
/// MySQL (<c>MATCH … AGAINST</c>); each requires a full-text index/catalog on the searched columns
/// (the developer creates it — see the provider docs). Not supported on SQLite or Oracle in v1 (a
/// compile-time diagnostic is reported).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryFullTextSearchAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InquiryFullTextSearchAttribute"/> class.</summary>
    /// <param name="columns">One or more mapped property or column names to search.</param>
    public InquiryFullTextSearchAttribute(params string[] columns)
    {
        if (columns is null || columns.Length == 0)
        {
            throw new ArgumentException("At least one column must be supplied.", nameof(columns));
        }

        for (var i = 0; i < columns.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(columns[i]))
            {
                throw new ArgumentException("Column names cannot be empty.", nameof(columns));
            }
        }

        Columns = columns;
    }

    /// <summary>Gets the mapped property or column names to search, in declaration order.</summary>
    public IReadOnlyList<string> Columns { get; }
}
