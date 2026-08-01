using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

/// <summary>
/// Symbol-free, value-equatable replacement for the old <c>TypeInfo</c>. Carries everything the
/// emitters need about a property/parameter type (display names, special-type classification,
/// nullability, value-type-ness, enum/Guid flags) so no <see cref="ITypeSymbol"/> survives into the
/// cached models. <see cref="DisplayName"/> uses <c>FullyQualifiedFormat</c> (no nullable-reference
/// annotation) and therefore doubles as a comparison key equivalent to
/// <c>SymbolEqualityComparer.Default</c> for the parameter/column type checks.
/// </summary>
internal sealed record TypeData(
    string DisplayName,
    string NonNullableDisplayName,
    SpecialType SpecialType,
    SpecialType EnumUnderlyingSpecialType,
    bool IsNullable,
    bool IsValueType,
    bool IsGuid,
    bool IsEnum)
{
    /// <summary>True when the type is <c>byte[]</c> (or nullable <c>byte[]</c>), mapped to a binary/BLOB column.</summary>
    public bool IsByteArray { get; init; }

    /// <summary>True when the type is <see cref="System.DateOnly"/> (or <c>DateOnly?</c>). Like
    /// <see cref="IsGuid"/>, matched by display name because the type has no <see cref="SpecialType"/>.</summary>
    public bool IsDateOnly { get; init; }

    /// <summary>True when the type is <see cref="System.TimeOnly"/> (or <c>TimeOnly?</c>). Like
    /// <see cref="IsGuid"/>, matched by display name because the type has no <see cref="SpecialType"/>.</summary>
    public bool IsTimeOnly { get; init; }

    /// <summary>Builds a <see cref="TypeData"/> from a type symbol. Called only during discovery —
    /// the result holds no symbol. Mirrors the old <c>TypeInfo.Create</c> exactly.</summary>
    public static TypeData Create(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
    {
        var nonNullable = GetNonNullableSymbol(symbol);
        var isByteArray = nonNullable is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };
        var isEnum = nonNullable.TypeKind == TypeKind.Enum;
        var enumUnderlyingSpecialType = isEnum && nonNullable is INamedTypeSymbol named
            ? named.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32
            : SpecialType.None;
        var nonNullableDisplay = nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new TypeData(
            DisplayName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NonNullableDisplayName: nonNullableDisplay,
            SpecialType: nonNullable.SpecialType,
            EnumUnderlyingSpecialType: enumUnderlyingSpecialType,
            IsNullable: DetermineIsNullable(symbol, nullableAnnotation),
            IsValueType: symbol.IsValueType,
            IsGuid: nonNullableDisplay == "global::System.Guid",
            IsEnum: isEnum)
        {
            IsByteArray = isByteArray,
            IsDateOnly = nonNullableDisplay == "global::System.DateOnly",
            IsTimeOnly = nonNullableDisplay == "global::System.TimeOnly",
        };
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
