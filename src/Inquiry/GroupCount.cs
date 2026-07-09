namespace Inquiry;

/// <summary>
/// Represents a single row from a <c>SELECT col, COUNT(*) … GROUP BY col</c> query —
/// the key value and its associated count. Returned by methods decorated with
/// <see cref="Stores.InquiryGroupCountAttribute"/>.
/// </summary>
/// <typeparam name="TKey">The type of the grouped column.</typeparam>
public sealed class GroupCount<TKey>
{
    /// <summary>Initializes a new instance of the <see cref="GroupCount{TKey}"/> class.</summary>
    public GroupCount(TKey key, long count)
    {
        Key = key;
        Count = count;
    }

    /// <summary>Gets the grouped column value.</summary>
    public TKey Key { get; }

    /// <summary>Gets the row count for this group.</summary>
    public long Count { get; }
}
