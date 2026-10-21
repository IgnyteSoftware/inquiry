namespace Inquiry.Entities;

/// <summary>Declares a raw provider-SQL table check constraint.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InquiryCheckAttribute : Attribute
{
    /// <summary>Initializes a check using the supplied raw SQL expression.</summary>
    public InquiryCheckAttribute(string expression) => Expression = expression;
    /// <summary>Gets the raw provider SQL expression.</summary>
    public string Expression { get; }
    /// <summary>Gets or sets the physical constraint name.</summary>
    public string? Name { get; set; }
}
