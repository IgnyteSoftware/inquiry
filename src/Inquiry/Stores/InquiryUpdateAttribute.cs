namespace Inquiry.Stores;

/// <summary>
/// Generates a method that updates an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the generated method returns the row produced by the database.
    /// </summary>
    public bool ReturnEntity { get; set; }
}
