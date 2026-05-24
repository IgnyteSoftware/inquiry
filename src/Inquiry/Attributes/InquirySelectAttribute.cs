namespace Inquiry;

/// <summary>
/// Generates a method that selects all rows for the store entity.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAttribute : Attribute
{
}
