namespace Inquiry;

/// <summary>
/// Marks the single primary key property for an Inquiry entity.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryKeyAttribute : InquiryColumnAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryKeyAttribute"/> class.
    /// </summary>
    public InquiryKeyAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryKeyAttribute"/> class.
    /// </summary>
    public InquiryKeyAttribute(string name)
        : base(name)
    {
    }
}
