namespace Inquiry.Entities;

/// <summary>
/// Stores a property as JSON text. Apply alongside <c>[InquiryColumn]</c> on a property whose column is
/// a text type; the value is serialized/deserialized with <c>System.Text.Json</c> (a built-in value
/// converter). A null value maps to <c>NULL</c>.
/// </summary>
/// <remarks>
/// Without a serializer context, JSON serialization is reflection-based and therefore not trim/AOT-clean
/// (it triggers IL2026/IL3050 warnings under PublishTrimmed/AOT). For trimmed and NativeAOT applications,
/// set <see cref="Context"/> or apply <see cref="InquiryJsonContextAttribute"/> to the assembly so Inquiry
/// uses the metadata of a source-generated <c>JsonSerializerContext</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryJsonAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the source-generated <c>System.Text.Json.Serialization.JsonSerializerContext</c> that
    /// serializes this property. It overrides the assembly's <see cref="InquiryJsonContextAttribute"/>.
    /// The context must declare <c>[JsonSerializable(typeof(T))]</c> for the property type, or the build
    /// fails with <c>INQ110</c>.
    /// </summary>
    public Type? Context { get; set; }
}
