namespace Inquiry.Stores;

/// <summary>
/// Generates an explicit table-wide literal delete. The method takes only a
/// <see cref="System.Threading.CancellationToken"/> and returns the number of affected rows.
/// </summary>
/// <remarks>Global filters continue to enforce their configured write behavior.</remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryHardDeleteAllAttribute : Attribute
{
}
