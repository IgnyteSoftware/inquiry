namespace Inquiry.Stores;

/// <summary>
/// Generates a method that updates an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAttribute : Attribute
{
}
