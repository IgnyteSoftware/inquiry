namespace Inquiry.Entities;

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

    /// <summary>
    /// Gets or sets a value indicating whether the database generates the key
    /// (for example, IDENTITY or AUTOINCREMENT). Generated keys are excluded
    /// from INSERT statements.
    /// </summary>
    public bool IsGenerated { get; set; }
}
