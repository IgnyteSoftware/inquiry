namespace Inquiry.Stores;

/// <summary>
/// Generates a batch insert: a single multi-row <c>INSERT … VALUES (…),(…),…</c> for every item in
/// the supplied collection. The method takes an <see cref="System.Collections.Generic.IEnumerable{T}"/>
/// (or <c>IReadOnlyList&lt;T&gt;</c>) of the entity plus a <see cref="System.Threading.CancellationToken"/>
/// and returns <c>Task&lt;int&gt;</c> (total rows affected). An empty collection is a no-op returning 0.
/// </summary>
/// <remarks>
/// Wrap a multi-statement call in <c>BeginTransactionAsync</c> for atomicity; a single batch statement
/// is itself atomic. Database-generated and database-default columns are omitted from the insert.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryInsertAllAttribute : Attribute
{
}
