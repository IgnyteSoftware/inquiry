namespace Inquiry.Stores;

/// <summary>
/// The sort direction of a keyset (cursor) page. <see cref="Forward"/> walks ascending key order with
/// a <c>&gt;</c> comparison; <see cref="Backward"/> walks descending key order with a <c>&lt;</c>
/// comparison.
/// </summary>
public enum KeysetDirection
{
    /// <summary>Ascending key order, fetching rows whose key is greater than the cursor.</summary>
    Forward,

    /// <summary>Descending key order, fetching rows whose key is less than the cursor.</summary>
    Backward,
}

/// <summary>
/// Generates a keyset-paginated query over the store entity. The method must return
/// <c>Task&lt;InquiryPage&lt;TEntity, TCursor&gt;&gt;</c>, and take a nullable cursor parameter followed
/// by an <c>int pageSize</c> ahead of the cancellation token. The cursor is compared against the
/// listed key fields; on the first page a null cursor selects from the start. <c>pageSize + 1</c> rows
/// are requested so the generated body can report <see cref="Paging.InquiryPage{TEntity,TCursor}.HasMore"/>
/// without a second query.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryKeysetPageAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryKeysetPageAttribute"/> class.
    /// </summary>
    /// <param name="keyFields">
    /// One or more mapped property or column names forming the keyset ordering, most-significant first.
    /// At least one must be supplied.
    /// </param>
    public InquiryKeysetPageAttribute(params string[] keyFields)
    {
        if (keyFields is null || keyFields.Length == 0)
        {
            throw new ArgumentException("At least one key field must be supplied.", nameof(keyFields));
        }

        for (var i = 0; i < keyFields.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(keyFields[i]))
            {
                throw new ArgumentException("Key field names cannot be empty.", nameof(keyFields));
            }
        }

        KeyFields = keyFields;
    }

    /// <summary>Gets the keyset key fields, most-significant first.</summary>
    public IReadOnlyList<string> KeyFields { get; }

    /// <summary>Gets or sets the paging direction. Defaults to <see cref="KeysetDirection.Forward"/>.</summary>
    public KeysetDirection Direction { get; set; } = KeysetDirection.Forward;
}
