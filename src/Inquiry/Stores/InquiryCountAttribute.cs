namespace Inquiry.Stores;

/// <summary>
/// Generates a method returning the row count (<c>SELECT COUNT(*)</c>) for the entity's table.
/// The method must return <c>Task&lt;long&gt;</c> and take only a <see cref="System.Threading.CancellationToken"/>.
/// When the entity declares an <c>[InquirySoftDelete]</c> column, the count respects the active-row
/// filter (soft-deleted rows are excluded).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryCountAttribute : Attribute
{
}
