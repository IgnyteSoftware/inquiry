using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable, symbol-free description of the value converter applied to a column. The
/// generated materializer reads the provider primitive and calls <c>FromProvider</c>; the binder calls
/// <c>ToProvider</c> and binds the provider value. Stateless converters are instantiated inline at each
/// use site (<c>new TConverter()</c>).
/// </summary>
internal sealed record ConverterData(
    string ConverterTypeDisplay,
    string ProviderTypeDisplay,
    SpecialType ProviderSpecialType);
