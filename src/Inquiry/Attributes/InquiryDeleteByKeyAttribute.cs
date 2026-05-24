namespace Inquiry;

/// <summary>
/// Generates a method that deletes an entity by primary key.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteByKeyAttribute : Attribute
{
}
