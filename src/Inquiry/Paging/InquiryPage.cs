using System.Collections.Generic;

namespace Inquiry.Paging;

/// <summary>
/// One page of a keyset-paginated query: the materialized <see cref="Items"/>, the
/// <see cref="NextCursor"/> to pass back for the following page, and <see cref="HasMore"/> indicating
/// whether further rows exist. The generated store requests <c>pageSize + 1</c> rows, trims the extra,
/// and derives the cursor from the last returned row's key — no second round-trip.
/// </summary>
/// <typeparam name="TEntity">The mapped entity type.</typeparam>
/// <typeparam name="TCursor">
/// The cursor type: the key field value, or a value tuple for composite keysets. Constrained to
/// <c>struct</c> so <see cref="NextCursor"/> can be the open-page sentinel <c>null</c>.
/// </typeparam>
public readonly struct InquiryPage<TEntity, TCursor>
    where TEntity : class
    where TCursor : struct
{
    /// <summary>Initializes a new instance of the <see cref="InquiryPage{TEntity, TCursor}"/> struct.</summary>
    /// <param name="items">The page items, in sort order.</param>
    /// <param name="nextCursor">The cursor for the next page, or <see langword="null"/> when the page is empty.</param>
    /// <param name="hasMore">Whether more rows exist beyond this page.</param>
    public InquiryPage(IReadOnlyList<TEntity> items, TCursor? nextCursor, bool hasMore)
    {
        Items = items ?? System.Array.Empty<TEntity>();
        NextCursor = nextCursor;
        HasMore = hasMore;
    }

    /// <summary>
    /// Gets the page items, in sort order, trimmed to the requested page size. Never <see langword="null"/>
    /// for a constructed page; <c>default(InquiryPage&lt;,&gt;)</c> leaves it null, so prefer the constructor.
    /// </summary>
    public IReadOnlyList<TEntity> Items { get; }

    /// <summary>
    /// Gets the cursor for the next page (the last item's key value), or <see langword="null"/> when
    /// the page is empty.
    /// </summary>
    public TCursor? NextCursor { get; }

    /// <summary>Gets a value indicating whether more rows exist beyond this page.</summary>
    public bool HasMore { get; }
}
