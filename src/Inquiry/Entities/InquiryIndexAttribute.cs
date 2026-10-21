namespace Inquiry.Entities;

/// <summary>Declares an ordered composite or covering index for an Inquiry table.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InquiryIndexAttribute : Attribute
{
    /// <summary>Initializes an index over the named mapped CLR properties.</summary>
    public InquiryIndexAttribute(params string[] properties) => Properties = properties ?? throw new ArgumentNullException(nameof(properties));
    /// <summary>Gets the ordered key property names.</summary>
    public string[] Properties { get; }
    /// <summary>Gets or sets the physical index name.</summary>
    public string? Name { get; set; }
    /// <summary>Gets or sets whether key tuples must be unique.</summary>
    public bool IsUnique { get; set; }
    /// <summary>Gets or sets non-key covering property names.</summary>
    public string[] Include { get; set; } = [];
}
