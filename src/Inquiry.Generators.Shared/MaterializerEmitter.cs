using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System.Text;

namespace Inquiry.Generators;

/// <summary>Emits allocation-neutral, compile-time provider-specific row materializers.</summary>
internal static class MaterializerEmitter
{
    public static void EmitMaterializeBody(StringBuilder source, EquatableArray<ColumnData> columns, string targetType, SqlBuilder sqlBuilder, string indent)
    {
        source.AppendLine($"{indent}return new {targetType}");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    {column.PropertyName} = {ReadExpression(column.Type, i, sqlBuilder, column.EnumAsString, column.Converter)},");
        }
        source.AppendLine($"{indent}}};");
    }

    public static string ReadExpression(TypeData type, int index, SqlBuilder sqlBuilder, bool enumAsString = false, ConverterData? converter = null, ReaderResultRole role = ReaderResultRole.Column)
    {
        var nonNullable = type.NonNullableDisplayName;
        var read = converter is not null
            ? ConverterInvocationEmitter.FromProvider(converter, PlainReadExpression(converter.ProviderType, converter.ProviderSpecialType, index, nonNullable, converter.ProviderTypeDisplay, sqlBuilder, role))
            : enumAsString
                ? $"global::System.Enum.Parse<{nonNullable}>({sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(index, nonNullable, "global::System.String", SpecialType.System_String, role))})"
                : type.IsEnum
                    ? EnumReadExpression(type.EnumUnderlyingSpecialType, nonNullable, index, sqlBuilder, role)
                    : PlainReadExpression(type, type.SpecialType, index, nonNullable, nonNullable, sqlBuilder, role);

        if (!type.IsNullable) return read;
        return type.IsValueType
            ? $"reader.IsDBNull({index}) ? ({type.DisplayName})null : {read}"
            : $"reader.IsDBNull({index}) ? null : {read}";
    }

    private static string EnumReadExpression(SpecialType underlying, string enumTypeName, int index, SqlBuilder sqlBuilder, ReaderResultRole role)
    {
        var storageType = StorageSpecialType(underlying);
        var call = sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(index, enumTypeName, StorageTypeName(storageType), storageType, role));
        return $"unchecked(({enumTypeName}){call})";
    }

    private static string PlainReadExpression(TypeData? type, SpecialType specialType, int index, string logicalTypeName, string providerTypeName, SqlBuilder sqlBuilder, ReaderResultRole role)
    {
        var primitive = sqlBuilder.BuildReaderExpression(new ReaderExpressionContext(
            index,
            logicalTypeName,
            providerTypeName,
            StorageSpecialType(specialType),
            role,
            ProviderIsGuid: type?.IsGuid == true,
            ProviderIsByteArray: type?.IsByteArray == true,
            ProviderIsDateOnly: type?.IsDateOnly == true,
            ProviderIsTimeOnly: type?.IsTimeOnly == true));

        return specialType switch
        {
            SpecialType.System_SByte => $"unchecked((sbyte){primitive})",
            SpecialType.System_UInt16 => $"unchecked((ushort){primitive})",
            SpecialType.System_UInt32 => $"unchecked((uint){primitive})",
            SpecialType.System_UInt64 => $"unchecked((ulong){primitive})",
            _ => primitive,
        };
    }

    private static SpecialType StorageSpecialType(SpecialType type) => type switch
    {
        SpecialType.System_SByte => SpecialType.System_Byte,
        SpecialType.System_UInt16 => SpecialType.System_Int16,
        SpecialType.System_UInt32 => SpecialType.System_Int32,
        SpecialType.System_UInt64 => SpecialType.System_Int64,
        _ => type,
    };

    private static string StorageTypeName(SpecialType type) => type switch
    {
        SpecialType.System_Byte => "global::System.Byte",
        SpecialType.System_Int16 => "global::System.Int16",
        SpecialType.System_Int32 => "global::System.Int32",
        SpecialType.System_Int64 => "global::System.Int64",
        _ => "global::System.Int32",
    };
}
