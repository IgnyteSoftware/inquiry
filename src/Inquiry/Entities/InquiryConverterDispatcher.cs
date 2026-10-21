using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Inquiry.Entities;

/// <summary>Dispatches generated converter calls through their selected closed interface contract.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryConverterDispatcher<TConverter, TModel, TProvider>
    where TConverter : IInquiryValueConverter<TModel, TProvider>, new()
{
    /// <summary>Converts a model value to its provider representation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Published API called by generated code; the generic type arguments select the converter at compile time.")]
    public static TProvider ToProvider(TModel model)
        => InquiryConverterCache<TConverter>.Instance.ToProvider(model);

    /// <summary>Converts a provider value to its model representation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Published API called by generated code; the generic type arguments select the converter at compile time.")]
    public static TModel FromProvider(TProvider provider)
        => InquiryConverterCache<TConverter>.Instance.FromProvider(provider);
}
