using System.ComponentModel;

namespace Inquiry.Entities;

/// <summary>
/// Caches one shared instance per value-converter type for generated code. Converters are
/// stateless by contract (see <see cref="IInquiryValueConverter{TModel, TProvider}"/>), so the
/// generated materializers and parameter binders read/write through this single instance instead
/// of allocating a converter per column per row.
/// </summary>
/// <typeparam name="TConverter">The converter type; must have a public parameterless constructor.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryConverterCache<TConverter>
    where TConverter : new()
{
    /// <summary>The shared converter instance.</summary>
    public static readonly TConverter Instance = new();
}
