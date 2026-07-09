namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects all rows for the store entity.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a compile-time ORDER BY specification, e.g. <c>"Name ASC, Id DESC"</c>. Each item
    /// is <c>field [ASC|DESC]</c>; fields are resolved against the entity's mapped properties or columns
    /// and quoted at generation time (an unknown field is a compile error). Direction defaults to ASC.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the method emits an offset-paginated query
    /// (<c>LIMIT/OFFSET</c>, or <c>OFFSET … FETCH</c> on SQL Server). When true the method must take an
    /// <c>int offset</c> and <c>int limit</c> (in that order) ahead of the cancellation token, and
    /// <see cref="OrderBy"/> is required for a deterministic page order.
    /// </summary>
    public bool Paged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the query auto-appends the active filter (<c>= 0</c> / <c>IS NULL</c>); when true the
    /// query is emitted unfiltered so soft-deleted rows are returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query uses <c>SELECT DISTINCT</c> instead
    /// of <c>SELECT</c>. Most valuable on projection-returning methods (distinct <c>Country</c> values)
    /// and on column-subset field selects. On an entity with a key column, a full-column select is
    /// already unique per row, so <c>Distinct</c> is redundant there — the database still deduplicates,
    /// just without effect.
    /// </summary>
    public bool Distinct { get; set; }
}
