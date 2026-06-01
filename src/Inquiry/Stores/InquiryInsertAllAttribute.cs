namespace Inquiry.Stores;

/// <summary>
/// Generates a batch insert: a single multi-row <c>INSERT … VALUES (…),(…),…</c> for every item in
/// the supplied collection. The method takes an <see cref="System.Collections.Generic.IEnumerable{T}"/>
/// (or <c>IReadOnlyList&lt;T&gt;</c>) of the entity plus a <see cref="System.Threading.CancellationToken"/>
/// and returns <c>Task&lt;int&gt;</c> (total rows affected). An empty collection is a no-op returning 0.
/// </summary>
/// <remarks>
/// A single batch statement is itself atomic. Database-generated, database-default, and
/// database-generated concurrency-token columns are omitted from the insert.
/// <para>
/// LIMIT: the whole collection becomes one statement with <c>rows × insertable-columns</c> bound
/// parameters, so a call must stay under the provider's parameter cap (SQL Server's is 2100). Chunk
/// large collections at the call site until configurable batch sizing lands.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryInsertAllAttribute : Attribute
{
}
