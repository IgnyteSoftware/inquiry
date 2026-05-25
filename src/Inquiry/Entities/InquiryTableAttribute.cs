namespace Inquiry.Entities;

/// <summary>
/// Maps a CLR entity type to a relational database table.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class InquiryTableAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryTableAttribute"/> class,
    /// inferring the table name from the CLR type name.
    /// </summary>
    public InquiryTableAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryTableAttribute"/> class
    /// with an explicit table name.
    /// </summary>
    public InquiryTableAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Table name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the mapped table name, or <see langword="null"/> to use the CLR type name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets or sets the optional database schema name.
    /// </summary>
    public string? Schema { get; init; }
}
