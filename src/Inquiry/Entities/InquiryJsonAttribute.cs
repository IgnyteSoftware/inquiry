namespace Inquiry.Entities;

/// <summary>
/// Stores a property as JSON text. Apply alongside <c>[InquiryColumn]</c> on a property whose column is
/// a text type; the value is serialized/deserialized with <c>System.Text.Json</c> (a built-in value
/// converter). A null value maps to <c>NULL</c>.
/// </summary>
/// <remarks>
/// JSON serialization is reflection-based and therefore not trim/AOT-clean in v1 (it may trigger
/// IL2026/IL3050 warnings under PublishTrimmed/AOT). Use a custom <see cref="IInquiryValueConverter{TModel,TProvider}"/>
/// with a source-generated <c>JsonSerializerContext</c> if AOT safety is required.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryJsonAttribute : Attribute
{
}
