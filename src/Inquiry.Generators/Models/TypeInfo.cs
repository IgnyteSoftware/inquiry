using Microsoft.CodeAnalysis;

namespace Inquiry.Generators;

internal sealed class TypeInfo
{
    private TypeInfo(ITypeSymbol symbol, SpecialType specialType, bool isNullable, bool isGuid, bool isDateTimeOffset, bool isByteArray)
    {
        Symbol = symbol;
        SpecialType = specialType;
        IsNullable = isNullable;
        IsGuid = isGuid;
        IsDateTimeOffset = isDateTimeOffset;
        IsByteArray = isByteArray;
        IsSupported = IsSupportedType(specialType, isGuid, isDateTimeOffset, isByteArray);
        DisplayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        NonNullableDisplayName = GetNonNullableSymbol(symbol).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public ITypeSymbol Symbol { get; }

    public SpecialType SpecialType { get; }

    public bool IsNullable { get; }

    public bool IsGuid { get; }

    public bool IsDateTimeOffset { get; }

    public bool IsByteArray { get; }

    public bool IsSupported { get; }

    public string DisplayName { get; }

    public string NonNullableDisplayName { get; }

    public static TypeInfo Create(ITypeSymbol symbol, NullableAnnotation nullableAnnotation)
    {
        var nonNullable = GetNonNullableSymbol(symbol);
        return new TypeInfo(
            symbol,
            nonNullable.SpecialType,
            DetermineIsNullable(symbol, nullableAnnotation),
            nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid",
            nonNullable.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.DateTimeOffset",
            nonNullable is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte });
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

    private static bool IsSupportedType(SpecialType specialType, bool isGuid, bool isDateTimeOffset, bool isByteArray)
    {
        return specialType is SpecialType.System_String
            or SpecialType.System_Int16
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Boolean
            or SpecialType.System_Decimal
            or SpecialType.System_Double
            or SpecialType.System_Single
            or SpecialType.System_DateTime
            || isGuid
            || isDateTimeOffset
            || isByteArray;
    }
}
