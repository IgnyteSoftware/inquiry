namespace Inquiry.Stores;

/// <summary>
/// Generates a method that restores a soft-deleted entity by primary key — an UPDATE that clears the
/// <c>[InquirySoftDelete]</c> indicator (flag back to <c>0</c>/<c>false</c>, timestamp back to
/// <c>NULL</c>). The method takes the key parameter(s) and returns <c>Task&lt;bool&gt;</c>
/// (true when a row was affected). Requires the entity to declare an <c>[InquirySoftDelete]</c> column.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryRestoreOneByKeyAttribute : Attribute
{
}
