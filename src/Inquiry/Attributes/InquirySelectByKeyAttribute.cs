namespace Inquiry;

/// <summary>
/// Generates a method that selects a single row by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectByKeyAttribute : Attribute
{
}
