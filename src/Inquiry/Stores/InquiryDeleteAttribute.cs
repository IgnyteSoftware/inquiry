namespace Inquiry.Stores;

/// <summary>
/// Generates a delete method. Without <see cref="InquiryWhereAttribute"/> criteria, the method
/// deletes one row by primary key. With one or more criteria, it deletes every matching row.
/// A targetless delete is rejected; use <see cref="InquiryDeleteAllAttribute"/> to delete every row.
/// A key-based method returns either <see cref="bool"/> or the deleted entity; the return type selects
/// the generated command shape. Use <see cref="InquiryHardDeleteAttribute"/> to bypass an entity's
/// soft-delete behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteAttribute : Attribute
{
}
