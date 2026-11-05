namespace Inquiry.Stores;

/// <summary>
/// Generates a method returning the row count (<c>SELECT COUNT(*)</c>) for the entity's table.
/// The method must return <c>Task&lt;long&gt;</c> and end with a <see cref="System.Threading.CancellationToken"/>.
/// Add <see cref="InquiryWhereAttribute"/> criteria, directly or through a specification attribute,
/// to count only the matching rows. Value criteria bind to the method's parameters as they do on
/// <see cref="InquirySelectAllByPredicateAttribute"/>. When the entity declares an
/// <c>[InquirySoftDelete]</c> column, the count respects the active-row filter (soft-deleted rows are
/// excluded).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryCountAttribute : Attribute
{
}
