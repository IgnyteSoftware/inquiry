namespace Inquiry.Entities;

/// <summary>
/// Stores an <c>enum</c> property as its member name (a string) rather than its underlying integer.
/// Apply alongside <c>[InquiryColumn]</c> on an enum (or nullable enum) property whose column is a
/// text type. The generated materializer reads the column with <c>Enum.Parse</c>, and inserts/updates
/// bind the enum's name. A null nullable-enum maps to <c>NULL</c>.
/// <para>
/// Intended for regular data columns (including WHERE-clause filters). Using it on a
/// <c>[InquiryKey]</c> column or a keyset-pagination cursor column is not supported: those binding
/// paths still bind the enum's underlying integer.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryEnumAsStringAttribute : Attribute
{
}
