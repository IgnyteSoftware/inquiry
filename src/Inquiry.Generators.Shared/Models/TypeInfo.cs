using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class TypeInfo
{
    private TypeInfo(
        ITypeSymbol symbol,
        SpecialType specialType,
        bool isNullable,
        bool isGuid,
        bool isEnum,
        SpecialType enumUnderlyingSpecialType)
    {
        Symbol = symbol;
        SpecialType = specialType;
        IsNullable = isNullable;
        IsGuid = isGuid;
        IsEnum = isEnum;
        EnumUnderlyingSpecialType = enumUnderlyingSpecialType;
        DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        NonNullableDisplayName = GetNonNullableSymbol(symbol).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public ITypeSymbol Symbol { get; }

    public SpecialType SpecialType { get; }

    public bool IsNullable { get; }

    public bool IsGuid { get; }

    public bool IsEnum { get; }

    public SpecialType EnumUnderlyingSpecialType { get; }

    public string DisplayName { get; }

    public string NonNullableDisplayName { get; }

    public static TypeInfo Create(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
    {
        var nonNullable = GetNonNullableSymbol(symbol);
        var isEnum = nonNullable.TypeKind == TypeKind.Enum;
        var enumUnderlyingSpecialType = isEnum && nonNullable is INamedTypeSymbol named
            ? named.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32
            : SpecialType.None;

        return new TypeInfo(
            symbol,
            nonNullable.SpecialType,
            DetermineIsNullable(symbol, nullableAnnotation),
            nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid",
            isEnum,
            enumUnderlyingSpecialType);
    }

    private static bool DetermineIsNullable(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
    {
        return nullableAnnotation == NullableAnnotation.Annotated ||
            symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    private static ITypeSymbol GetNonNullableSymbol(ITypeSymbol symbol)
    {
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
        {
            return named.TypeArguments[0];
        }

        return symbol;
    }
}
