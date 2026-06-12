using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Inquiry.Entities;

namespace Inquiry.Converters;

/// <summary>
/// Built-in value converter backing <c>[InquiryJson]</c>: serializes a value to/from JSON text with
/// <c>System.Text.Json</c> using default options. Stateless with a public parameterless constructor so
/// the generator can instantiate it.
/// </summary>
/// <typeparam name="T">The property type stored as JSON.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[RequiresUnreferencedCode("Reflection-based System.Text.Json serialization may break under trimming. For trimmed/NativeAOT applications, use a custom IInquiryValueConverter that serializes via a JsonSerializerContext.")]
[RequiresDynamicCode("Reflection-based System.Text.Json serialization may require runtime code generation. For NativeAOT applications, use a custom IInquiryValueConverter that serializes via a JsonSerializerContext.")]
public sealed class InquiryJsonConverter<T> : IInquiryValueConverter<T, string>
{
    /// <inheritdoc/>
    public string ToProvider(T model) => JsonSerializer.Serialize(model);

    /// <inheritdoc/>
    public T FromProvider(string provider) => JsonSerializer.Deserialize<T>(provider)!;
}
