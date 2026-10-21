namespace Inquiry.Stores;

/// <summary>
/// Generates a literal delete method. Without <see cref="InquiryWhereAttribute"/> criteria, the method
/// deletes one row by primary key. With one or more criteria, it deletes every matching row.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryHardDeleteAttribute : Attribute
{
}
