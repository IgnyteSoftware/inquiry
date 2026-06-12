namespace Inquiry.Stores;

/// <summary>
/// Generates a method that deletes every row matching one or more <see cref="InquiryWhereAttribute"/>
/// criteria — the set-based analog of <see cref="InquiryDeleteOneByKeyAttribute"/> (compare EF Core's
/// <c>ExecuteDelete</c>). The criteria bind positionally to the method's
/// non-<see cref="System.Threading.CancellationToken"/> parameters in declaration order, and the
/// method returns <c>Task&lt;int&gt;</c> — the number of rows affected.
/// </summary>
/// <remarks>
/// At least one <see cref="InquiryWhereAttribute"/> criterion is required: an unfiltered set-based
/// delete is almost certainly a bug and is rejected at compile time. Use
/// <see cref="InquiryDeleteAllAttribute"/> to delete a whole key collection instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteWhereAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the method emits a literal <c>DELETE</c> even when the
    /// entity declares an <c>[InquirySoftDelete]</c> column. When false (the default) a soft-delete
    /// entity is deleted via an UPDATE that sets the indicator on every matching active row; when true
    /// the matching rows are physically removed. Has no effect on entities without a soft-delete
    /// column (always a literal <c>DELETE</c>).
    /// </summary>
    public bool HardDelete { get; set; }
}
