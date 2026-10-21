namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows matching one or more <see cref="InquiryWhereAttribute"/>
/// criteria. The criteria are combined (AND by default, OR opt-in per criterion) into the WHERE
/// clause and bind positionally to the method's parameters in declaration order.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByPredicateAttribute : Attribute
{
    /// <summary>Gets or sets a compile-time ORDER BY specification.</summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the method emits an offset-paginated query. A paged
    /// method takes <c>int offset</c> and <c>int limit</c> after its predicate parameters.
    /// </summary>
    public bool Paged { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the soft-delete active filter is AND-composed with the predicate WHERE; when true the
    /// query is emitted with only the predicate WHERE so soft-deleted rows are returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated query uses <c>SELECT DISTINCT</c> instead
    /// of <c>SELECT</c>.
    /// </summary>
    public bool Distinct { get; set; }

    /// <summary>
    /// Gets or sets the row-level lock mode for the generated query. The default is
    /// <see cref="InquiryLockMode.None"/> (no locking). Use within a transaction to acquire
    /// pessimistic locks on the selected rows.
    /// </summary>
    public InquiryLockMode LockMode { get; set; }
}
