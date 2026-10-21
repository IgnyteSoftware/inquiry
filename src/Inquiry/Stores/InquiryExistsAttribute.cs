namespace Inquiry.Stores;

/// <summary>
/// Generates a method returning whether any row exists — the <c>EXISTS</c> / EF <c>.AnyAsync()</c>
/// analog. The method must return <c>Task&lt;bool&gt;</c>. Apply zero or more
/// <see cref="InquiryWhereAttribute"/> criteria (exactly as on <c>[InquirySelectAllByPredicate]</c>) to
/// test for a matching row; with no criteria it tests whether the table has any row at all.
/// </summary>
/// <remarks>
/// Renders <c>SELECT CASE WHEN EXISTS(SELECT 1 FROM … WHERE …) THEN 1 ELSE 0 END</c>, which short-circuits
/// at the first match — cheaper than a <c>COUNT(*) &gt; 0</c>. When the entity declares an
/// <c>[InquirySoftDelete]</c> or <c>[InquiryGlobalFilter]</c> column, the test respects the active-row
/// filter (hidden rows don't count as existing).
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryExistsAttribute : Attribute
{
}
