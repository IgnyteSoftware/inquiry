namespace Inquiry.Stores;

/// <summary>
/// Generates a method that selects rows matching one or more <see cref="InquiryWhereAttribute"/>
/// criteria. The criteria are combined (AND by default, OR opt-in per criterion) into the WHERE
/// clause and bind positionally to the method's parameters in declaration order.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquirySelectAllByPredicateAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the generated query includes soft-deleted rows. Has an
    /// effect only when the entity declares an <c>[InquirySoftDelete]</c> column: when false (the
    /// default) the soft-delete active filter is AND-composed with the predicate WHERE; when true the
    /// query is emitted with only the predicate WHERE so soft-deleted rows are returned.
    /// </summary>
    public bool IncludeDeleted { get; set; }
}
