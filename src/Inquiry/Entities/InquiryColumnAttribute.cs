namespace Inquiry.Entities;

/// <summary>
/// Maps a CLR property to a relational database column.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class InquiryColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryColumnAttribute"/> class.
    /// </summary>
    public InquiryColumnAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryColumnAttribute"/> class.
    /// </summary>
    public InquiryColumnAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the mapped column name, or <see langword="null"/> to use the CLR property name.
    /// </summary>
    public string? Name { get; }
}
