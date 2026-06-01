namespace Inquiry.Entities;

/// <summary>
/// Stores an <c>enum</c> property as its member name (a string) rather than its underlying integer.
/// Apply alongside <c>[InquiryColumn]</c> on an enum (or nullable enum) property whose column is a
/// text type. The generated materializer reads the column with <c>Enum.Parse</c>, and inserts/updates
/// bind the enum's name. A null nullable-enum maps to <c>NULL</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryEnumAsStringAttribute : Attribute
{
}
