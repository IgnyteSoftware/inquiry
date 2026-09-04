namespace Inquiry.Stores;

/// <summary>
/// Generates a delete method. Without <see cref="InquiryWhereAttribute"/> criteria, the method
/// deletes one row by primary key. With one or more criteria, it deletes every matching row.
/// A targetless delete is rejected; use <see cref="InquiryDeleteAllAttribute"/> to delete every row.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether a key-based delete returns the deleted entity.
    /// This option is not supported when the method uses <see cref="InquiryWhereAttribute"/>.
    /// </summary>
    public bool ReturnEntity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the method emits a literal <c>DELETE</c> for an entity
    /// with an <c>[InquirySoftDelete]</c> column. The default form updates the soft-delete indicator.
    /// </summary>
    public bool HardDelete { get; set; }
}
