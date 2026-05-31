using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Infrastructure;

/// <summary>
/// Pure, symbol-free mapping from a <see cref="TypeData"/> to the fully-qualified
/// <c>System.Data.DbType</c> enum-member expression emitted onto generated parameters.
/// Threading the compile-time DbType into binders gives <c>DbCommand.Prepare()</c> the fixed
/// parameter types it needs (W4); without it providers re-infer the type on every call.
/// </summary>
/// <remarks>
/// <see cref="TypeData.SpecialType"/> and <see cref="TypeData.EnumUnderlyingSpecialType"/> are
/// already the non-nullable type's classification, so nullable value types map like their
/// underlying type. Enums coerce to their underlying integer (matching the binder's value
/// expression). Types with no portable DbType (custom value converters, byte[], unknown) return
/// <c>null</c> so no assignment is emitted and the provider falls back to its own inference.
/// </remarks>
internal static class DbTypeMapper
{
    public static string? TryGetDbTypeExpression(TypeData type)
    {
        if (type.IsGuid)
        {
            return "global::System.Data.DbType.Guid";
        }

        var special = type.IsEnum ? type.EnumUnderlyingSpecialType : type.SpecialType;
        return Map(special);
    }

    private static string? Map(SpecialType specialType) => specialType switch
    {
        SpecialType.System_Boolean => "global::System.Data.DbType.Boolean",
        SpecialType.System_Byte => "global::System.Data.DbType.Byte",
        SpecialType.System_SByte => "global::System.Data.DbType.SByte",
        SpecialType.System_Int16 => "global::System.Data.DbType.Int16",
        SpecialType.System_UInt16 => "global::System.Data.DbType.UInt16",
        SpecialType.System_Int32 => "global::System.Data.DbType.Int32",
        SpecialType.System_UInt32 => "global::System.Data.DbType.UInt32",
        SpecialType.System_Int64 => "global::System.Data.DbType.Int64",
        SpecialType.System_UInt64 => "global::System.Data.DbType.UInt64",
        SpecialType.System_Single => "global::System.Data.DbType.Single",
        SpecialType.System_Double => "global::System.Data.DbType.Double",
        SpecialType.System_Decimal => "global::System.Data.DbType.Decimal",
        SpecialType.System_String => "global::System.Data.DbType.String",
        SpecialType.System_Char => "global::System.Data.DbType.StringFixedLength",
        SpecialType.System_DateTime => "global::System.Data.DbType.DateTime",
        _ => null,
    };
}
