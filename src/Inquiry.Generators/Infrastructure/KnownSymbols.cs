using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Infrastructure;

internal static class KnownSymbols
{
    public const string EntityAttributeNamespace = "Inquiry.Entities";
    public const string StoreAttributeNamespace = "Inquiry.Stores";
    public const string StoreNamespace = "Inquiry.Stores";
    public const string GlobalPrefix = "global::";

    public static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
}
