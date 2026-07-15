using System.Collections.Generic;

namespace Inquiry.Paging;

/// <summary>
/// One page of an offset-paginated query paired with the total row count: the materialized
/// <see cref="Items"/> and the <see cref="TotalCount"/> from a separate <c>COUNT</c> query that
/// shares the same WHERE clause. Both queries are generated from the same source-of-truth so
/// their filters cannot diverge.
/// </summary>
/// <remarks>
/// The SELECT and COUNT execute as two sequential queries on the same connection without an
/// explicit transaction, so <see cref="TotalCount"/> may differ from the actual number of rows
/// if concurrent writes occur between the two queries. This matches the consistency model of
/// manually written paired queries.
/// </remarks>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
public readonly struct InquiryPagedResult<TEntity>
    where TEntity : class
{
    /// <summary>Initializes a new instance of the <see cref="InquiryPagedResult{TEntity}"/> struct.</summary>
    /// <param name="items">The page items, in sort order.</param>
    /// <param name="totalCount">The total number of rows matching the query filters (from the paired COUNT).</param>
    public InquiryPagedResult(IReadOnlyList<TEntity> items, long totalCount)
    {
        Items = items ?? System.Array.Empty<TEntity>();
        TotalCount = totalCount;
    }

    /// <summary>Gets the page items, in sort order, sized to the requested limit or fewer.</summary>
    public IReadOnlyList<TEntity> Items { get; }

    /// <summary>Gets the total number of rows matching the query filters, independent of offset/limit.</summary>
    public long TotalCount { get; }
}
