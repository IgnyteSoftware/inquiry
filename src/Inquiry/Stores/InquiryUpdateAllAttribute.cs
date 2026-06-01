namespace Inquiry.Stores;

/// <summary>
/// Generates a batch update: one <c>UPDATE … SET … WHERE key = …</c> statement per item in the
/// supplied collection, sent as a single multi-statement command (one round-trip). The method takes an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> of the entity plus a
/// <see cref="System.Threading.CancellationToken"/> and returns <c>Task&lt;int&gt;</c> (total rows
/// affected). An empty collection is a no-op returning 0.
/// </summary>
/// <remarks>
/// Each row is matched by its primary key and the same non-key columns the single-row update sets are
/// written. Batch update does NOT perform optimistic-concurrency checks (the concurrency token is
/// neither matched nor advanced), unlike <c>[InquiryUpdate]</c>. The whole collection becomes one
/// command with <c>rows × (set + key) columns</c> bound parameters, so a call must stay under the
/// provider's parameter cap; chunk large collections at the call site.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryUpdateAllAttribute : Attribute
{
}
