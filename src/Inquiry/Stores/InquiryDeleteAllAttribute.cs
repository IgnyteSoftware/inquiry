namespace Inquiry.Stores;

/// <summary>
/// Generates a batch delete: a single <c>DELETE FROM t WHERE key IN (…)</c> removing every row whose
/// key is in the supplied collection. The method takes an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> of the entity's key type plus a
/// <see cref="System.Threading.CancellationToken"/> and returns <c>Task&lt;int&gt;</c> (rows affected).
/// An empty collection matches no rows and returns 0.
/// </summary>
/// <remarks>
/// Single-key entities only (the <c>IN</c> list is over the one key column). For a soft-delete entity
/// the matched rows are soft-deleted (an <c>UPDATE</c> of the indicator) rather than physically
/// removed, mirroring <c>[InquiryDeleteOneByKey]</c>. The key collection becomes one bound parameter
/// per element, so a call must stay under the provider's parameter cap; chunk large collections at the
/// call site.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryDeleteAllAttribute : Attribute
{
}
