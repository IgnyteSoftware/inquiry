using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// Emits the body of an <c>IInquiryEntityMaterializer.Materialize</c> method: a <c>new T { ... }</c>
/// object initializer that reads each column from the <see cref="System.Data.Common.DbDataReader"/>
/// by ordinal. Extracted from <see cref="EntityProcessor"/> into a shared home so both
/// entity materializers and future projection materializers (which read a column subset into a DTO)
/// share one read-expression path. Reads by the column's position in the supplied list, so callers
/// must pass columns in the same order as the emitted <c>SELECT</c> list.
/// </summary>
internal static class MaterializerEmitter
{
    public static void EmitMaterializeBody(StringBuilder source, EquatableArray<ColumnData> columns, string targetType, string indent)
    {
        source.AppendLine($"{indent}return new {targetType}");
        source.AppendLine($"{indent}{{");
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            source.AppendLine($"{indent}    {column.PropertyName} = {ReadExpression(column.Type, i, column.EnumAsString, column.Converter)},");
        }
        source.AppendLine($"{indent}}};");
    }

    public static string ReadExpression(TypeData type, int index, bool enumAsString = false, ConverterData? converter = null)
    {
        var nonNullable = type.NonNullableDisplayName;
        // a converter reads the provider primitive and maps it back via FromProvider.
        // converters are stateless; read through the shared cached instance instead of allocating
        // one per column per row.
        var read = converter is not null
            ? $"global::Inquiry.Entities.InquiryConverterCache<{converter.ConverterTypeDisplay}>.Instance.FromProvider({ReadCallForSpecialType(converter.ProviderSpecialType, index, converter.ProviderTypeDisplay)})"
            : enumAsString
                ? $"global::System.Enum.Parse<{nonNullable}>(reader.GetString({index}))"
                : type.IsEnum
                    ? EnumReadExpression(type.EnumUnderlyingSpecialType, nonNullable, index)
                    : type.IsGuid
                        ? $"reader.GetGuid({index})"
                        // DbDataReader has no GetDateOnly/GetTimeOnly; GetFieldValue<T> is the
                        // documented read path for both on modern providers.
                        : type.IsDateOnly
                            ? $"reader.GetFieldValue<global::System.DateOnly>({index})"
                            : type.IsTimeOnly
                                ? $"reader.GetFieldValue<global::System.TimeOnly>({index})"
                                : PlainReadExpression(type.SpecialType, index, nonNullable);

        if (!type.IsNullable)
        {
            return read;
        }

        if (type.IsValueType)
        {
            return $"reader.IsDBNull({index}) ? ({type.DisplayName})null : {read}";
        }

        return $"reader.IsDBNull({index}) ? null : {read}";
    }

    // For enum columns: read via the signed storage GetXxx, then cast to the enum type.
    // The cast must be wrapped in unchecked(): an enum cast from an integral applies the consumer's
    // checked/unchecked context to the underlying conversion, so reinterpreting an out-of-range signed
    // value (e.g. byte 255 → sbyte-backed enum, or int -1 → uint-backed enum) would throw
    // OverflowException in a consumer compiled with CheckForOverflowUnderflow=true. unchecked() forces
    // the lossless bit reinterpretation the binding relies on.
    private static string EnumReadExpression(SpecialType underlying, string enumTypeName, int index)
    {
        var signedCall = underlying switch
        {
            SpecialType.System_SByte  => $"reader.GetByte({index})",   // byte storage → sbyte underlying
            SpecialType.System_UInt16 => $"reader.GetInt16({index})",  // short storage → ushort underlying
            SpecialType.System_UInt32 => $"reader.GetInt32({index})",  // int storage → uint underlying
            SpecialType.System_UInt64 => $"reader.GetInt64({index})",  // long storage → ulong underlying
            _ => null,
        };
        // Signed and unknown underlyings fall through to the regular typed read (also unchecked, harmless).
        var call = signedCall ?? ReadCallForSpecialType(underlying, index, enumTypeName);
        return $"unchecked(({enumTypeName}){call})";
    }

    // For plain (non-enum) columns: unsigned/sbyte types are stored as same-width signed values
    // and reinterpret-cast back with unchecked() on read. Providers return the signed storage type
    // as the boxed object; GetFieldValue<uint> would throw InvalidCastException on most providers.
    private static string PlainReadExpression(SpecialType specialType, int index, string fallbackTypeName)
    {
        return specialType switch
        {
            SpecialType.System_SByte  => $"unchecked((sbyte)reader.GetByte({index}))",
            SpecialType.System_UInt16 => $"unchecked((ushort)reader.GetInt16({index}))",
            SpecialType.System_UInt32 => $"unchecked((uint)reader.GetInt32({index}))",
            SpecialType.System_UInt64 => $"unchecked((ulong)reader.GetInt64({index}))",
            _ => ReadCallForSpecialType(specialType, index, fallbackTypeName),
        };
    }

    private static string ReadCallForSpecialType(SpecialType specialType, int index, string fallbackTypeName)
    {
        return specialType switch
        {
            SpecialType.System_String => $"reader.GetString({index})",
            SpecialType.System_Boolean => $"reader.GetBoolean({index})",
            SpecialType.System_Byte => $"reader.GetByte({index})",
            SpecialType.System_Char => $"reader.GetChar({index})",
            SpecialType.System_Int16 => $"reader.GetInt16({index})",
            SpecialType.System_Int32 => $"reader.GetInt32({index})",
            SpecialType.System_Int64 => $"reader.GetInt64({index})",
            SpecialType.System_Single => $"reader.GetFloat({index})",
            SpecialType.System_Double => $"reader.GetDouble({index})",
            SpecialType.System_Decimal => $"reader.GetDecimal({index})",
            SpecialType.System_DateTime => $"reader.GetDateTime({index})",
            _ => $"reader.GetFieldValue<{fallbackTypeName}>({index})",
        };
    }
}
