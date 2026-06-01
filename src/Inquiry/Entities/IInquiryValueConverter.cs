namespace Inquiry.Entities;

/// <summary>
/// Converts a non-primitive CLR property type to and from a provider-native primitive that Inquiry's
/// read/write paths already handle (e.g. <c>string</c>, <c>decimal</c>). Apply via
/// <c>[InquiryColumn(Converter = typeof(MyConverter))]</c>.
/// </summary>
/// <remarks>
/// Implementations MUST be stateless and have a public parameterless constructor — the generator
/// instantiates the converter in the generated materializer/binder. <typeparamref name="TProvider"/>
/// must be a type the column read/write paths support (a primitive or <c>string</c>).
/// </remarks>
/// <typeparam name="TModel">The CLR property type.</typeparam>
/// <typeparam name="TProvider">The provider-native primitive stored in the column.</typeparam>
public interface IInquiryValueConverter<TModel, TProvider>
{
    /// <summary>Converts the model value to the provider value written to the column.</summary>
    TProvider ToProvider(TModel model);

    /// <summary>Converts the provider value read from the column back to the model value.</summary>
    TModel FromProvider(TProvider provider);
}
