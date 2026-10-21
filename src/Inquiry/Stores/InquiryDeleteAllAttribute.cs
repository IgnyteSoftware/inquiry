namespace Inquiry.Stores;

/// <summary>
/// Generates an explicit table-wide delete. The method takes only a
/// <see cref="System.Threading.CancellationToken"/> and returns the number of affected rows.
/// </summary>
/// <remarks>
/// For a soft-delete entity, this operation marks every active row as deleted. Use
/// <see cref="InquiryHardDeleteAllAttribute"/> to emit a literal <c>DELETE</c>. Global filters continue
/// to enforce their configured write behavior.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteAllAttribute : Attribute
{
}
