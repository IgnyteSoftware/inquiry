namespace Inquiry.Paging;

/// <summary>
/// One page of an offset-paginated query paired with the total row count: the materialized
/// <see cref="Items"/> and the <see cref="TotalCount"/> from a <c>COUNT</c> statement that uses
/// the same WHERE clause, including soft-delete and global filters. Both statements are generated
/// from the same declaration, so their filters cannot diverge.
/// </summary>
/// <remarks>
/// <para>
/// A generated method sends the page <c>SELECT</c> and the <c>COUNT</c> as one command that returns
/// two result sets, and reads them in order. The method makes one round trip and uses one
/// connection: a connection from the pool, or the connection of the active Inquiry transaction.
/// The <c>COUNT</c> still runs on every call and counts every matching row.
/// </para>
/// <para>
/// The two statements are not atomic. Outside a snapshot or serializable transaction, a concurrent
/// write can commit between them, so <see cref="TotalCount"/> can disagree with <see cref="Items"/>.
/// To get a consistent pair, call the method inside a transaction at one of those isolation levels.
/// Inquiry does not start one for you.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
public readonly struct InquiryPagedResult<TEntity>
    where TEntity : class
{
    private readonly IReadOnlyList<TEntity>? _items;

    /// <summary>Initializes a new instance of the <see cref="InquiryPagedResult{TEntity}"/> struct.</summary>
    /// <param name="items">The page items, in sort order.</param>
    /// <param name="totalCount">The total number of rows matching the query filters (from the paired COUNT).</param>
    public InquiryPagedResult(IReadOnlyList<TEntity> items, long totalCount)
    {
        _items = items;
        TotalCount = totalCount;
    }

    /// <summary>Gets the page items, in sort order, sized to the requested limit or fewer. A default result exposes an empty collection.</summary>
    public IReadOnlyList<TEntity> Items => _items ?? System.Array.Empty<TEntity>();

    /// <summary>Gets the total number of rows matching the query filters, independent of offset/limit.</summary>
    public long TotalCount { get; }
}
