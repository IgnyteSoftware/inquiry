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
    public static TProvider ToProvider(TModel model)
        => InquiryConverterCache<TConverter>.Instance.ToProvider(model);

    /// <summary>Converts a provider value to its model representation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TModel FromProvider(TProvider provider)
        => InquiryConverterCache<TConverter>.Instance.FromProvider(provider);
}
