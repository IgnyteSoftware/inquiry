using System.Collections.Generic;

namespace Inquiry.Generators.Models;

/// <summary>
/// Emit-stage resolution of a select method's ORDER BY / pagination. Produced by
/// <c>StoreProcessor.TryValidateForEmit</c> (which has both the entity columns and the builder) and
/// consumed by <c>StoreOperationEmitter</c>. Plain (non-ordered, non-paged) selects have a null plan
/// and keep their existing shared-SQL fast paths.
/// </summary>
internal sealed class ResolvedSelectPlan
{
    /// <summary>
    /// The per-method SQL const field name holding the ordered/paged statement (e.g. <c>_sql_PageAsync</c>).
    /// Null for an ordered-but-not-paged method that simply appends ORDER BY to its shared SQL — in that
    /// case the emitter still references this field, so it is always set when a plan exists.
    /// </summary>
    public required string SqlFieldName { get; init; }

    /// <summary>The resolved ORDER BY columns paired with their direction, most-significant first.</summary>
    public required IReadOnlyList<(ColumnData Column, bool Descending)> OrderColumns { get; init; }

    /// <summary>The pagination mode.</summary>
    public required Pagination Pagination { get; init; }

    /// <summary>
    /// For keyset paging, the resolved cursor key columns (most-significant first). Empty otherwise.
    /// </summary>
    public IReadOnlyList<ColumnData> KeysetColumns { get; init; } = System.Array.Empty<ColumnData>();
}
