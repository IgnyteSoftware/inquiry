using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Inquiry.Entities;

namespace Inquiry.Converters;

/// <summary>
/// Serializes <c>[InquiryJson]</c> values when a <see cref="JsonSerializerContext"/> is selected with
/// <see cref="InquiryJsonAttribute.Context"/> or <see cref="InquiryJsonContextAttribute"/>. It uses the
/// context's source-generated <see cref="JsonTypeInfo{T}"/> metadata, so it is safe under trimming and
/// NativeAOT. It never falls back to reflection.
/// </summary>
/// <remarks>
/// Generated code passes the context's generated <c>Default</c> instance. That instance carries the
/// options from <see cref="JsonSourceGenerationOptionsAttribute"/>; a context created with its
/// parameterless constructor uses default options instead.
/// </remarks>
/// <typeparam name="TContext">The source-generated serializer context that owns the metadata for <typeparamref name="T"/>.</typeparam>
/// <typeparam name="T">The property type stored as JSON.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Published API called by generated code; the generic type arguments select the context and value type at compile time.")]
public static class InquiryJsonContextConverter<TContext, T>
    where TContext : JsonSerializerContext
{
    private static JsonTypeInfo<T>? _typeInfo;

    /// <summary>Serializes <paramref name="model"/> to JSON text.</summary>
    /// <param name="model">The value to serialize.</param>
    /// <param name="context">The context's generated <c>Default</c> instance.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="context"/> has no metadata for <typeparamref name="T"/>.</exception>
    public static string ToProvider(T model, TContext context) => JsonSerializer.Serialize(model, GetTypeInfo(context));

    /// <summary>Deserializes JSON text.</summary>
    /// <param name="provider">The stored JSON text.</param>
    /// <param name="context">The context's generated <c>Default</c> instance.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/> has no metadata for <typeparamref name="T"/>, or the stored text
    /// deserialized to <see langword="null"/>. Nullable JSON properties are stored as SQL <c>NULL</c> and
    /// never reach the converter, so a null result means the column holds the JSON literal <c>null</c>.
    /// </exception>
    public static T FromProvider(string provider, TContext context)
    {
        var value = JsonSerializer.Deserialize(provider, GetTypeInfo(context));
        if (value is null)
        {
            throw new InvalidOperationException(
                "Deserializing the stored JSON produced a null " + typeof(T).FullName +
                ". Store SQL NULL for absent values instead of the JSON literal 'null'.");
        }

        return value;
    }

    // Generated code always passes the same Default instance, so the first lookup can be cached.
    private static JsonTypeInfo<T> GetTypeInfo(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _typeInfo ??= context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ?? throw new InvalidOperationException(
            "The JSON serializer context " + typeof(TContext).FullName + " has no metadata for " + typeof(T).FullName +
            ". Add [JsonSerializable(typeof(" + typeof(T).Name + "))] to the context. Inquiry does not fall back to reflection-based serialization.");
    }
}
