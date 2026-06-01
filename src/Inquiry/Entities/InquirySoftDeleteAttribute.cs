namespace Inquiry.Entities;

/// <summary>
/// Marks a mapped column as the entity's soft-delete indicator. The representation is inferred from
/// the CLR property type: a <see cref="bool"/> is a flag (active = <c>0</c>/<c>false</c>), a nullable
/// <see cref="System.DateTime"/> or <see cref="System.DateTimeOffset"/> is a timestamp (active =
/// <c>NULL</c>). Any other type is a compile error, as is marking more than one property.
/// </summary>
/// <remarks>
/// This is an orthogonal marker — the property still needs <c>[InquiryColumn]</c> (or
/// <c>[InquiryKey]</c>). When present, generated <c>[InquiryDeleteOneByKey]</c> methods become a soft
/// UPDATE (unless <c>HardDelete = true</c>), and every generated SELECT auto-appends the active filter
/// (unless the select opts out with <c>IncludeDeleted = true</c>). The delete/restore timestamp is
/// sourced from the database clock (<c>CURRENT_TIMESTAMP</c> / <c>GETUTCDATE()</c> / <c>now()</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquirySoftDeleteAttribute : Attribute
{
}
