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

    /// <summary>
    /// DDL generation: whether generated <c>CREATE TABLE</c> DDL emits <c>FOREIGN KEY</c>
    /// constraints for columns mapped with <see cref="InquiryForeignKeyAttribute"/>. Default true.
    /// </summary>
    public bool GenerateForeignKeys { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this mapping participates in generated assembly schema DDL. Set to
    /// <see langword="false"/> when the physical table is managed by hand-authored migrations or
    /// another canonical mapping. Stores and materializers are unaffected. Default true.
    /// </summary>
    public bool GenerateDdl { get; set; } = true;
}
