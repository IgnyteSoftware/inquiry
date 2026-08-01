using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Infrastructure;

/// <summary>
/// Pure, symbol-free mapping from a <see cref="TypeData"/> to the fully-qualified
/// <c>System.Data.DbType</c> enum-member expression emitted onto generated parameters.
/// Threading the compile-time DbType into binders gives <c>DbCommand.Prepare()</c> the fixed
/// parameter types it needs; without it providers re-infer the type on every call.
/// </summary>
/// <remarks>
/// <see cref="TypeData.SpecialType"/> and <see cref="TypeData.EnumUnderlyingSpecialType"/> are
/// already the non-nullable type's classification, so nullable value types map like their
/// underlying type. Enums coerce to their underlying integer (matching the binder's value
/// expression). Types with no portable DbType (unknown custom types) return
/// <c>null</c> so no assignment is emitted and the provider falls back to its own inference.
/// </remarks>
internal static class DbTypeMapper
{
    public static string? TryGetDbTypeExpression(TypeData type, bool isUnicode = true)
    {
        if (type.IsByteArray)
        {
            return "global::System.Data.DbType.Binary";
        }

        if (type.IsGuid)
        {
            return "global::System.Data.DbType.Guid";
        }

        if (type.IsDateOnly)
        {
            return "global::System.Data.DbType.Date";
        }

        if (type.IsTimeOnly)
        {
            return "global::System.Data.DbType.Time";
        }

        if (type.NonNullableDisplayName == "global::System.DateTimeOffset")
        {
            return "global::System.Data.DbType.DateTimeOffset";
        }

        var special = type.IsEnum ? type.EnumUnderlyingSpecialType : type.SpecialType;
        return Map(special, isUnicode);
    }

    /// <summary>DbType expression for a converter's provider <see cref="SpecialType"/>, or null.</summary>
    public static string? TryGetDbTypeForSpecialType(SpecialType specialType, bool isUnicode = true) => Map(specialType, isUnicode);

    // Unsigned CLR types (sbyte/ushort/uint/ulong) are bound via the same-width signed storage type.
    // DbType.SByte / UInt16 / UInt32 / UInt64 are rejected by Microsoft.Data.SqlClient (and several
    // other providers) at bind time with ArgumentException. Reinterpreting the bit pattern into the
    // signed partner is lossless — e.g. uint 3_000_000_000 ↔ int -1_294_967_296 — and the materializer
    // reverses the cast with unchecked() on read. Enum underlyings go through this same mapping because
    // TryGetDbTypeExpression routes enum types through EnumUnderlyingSpecialType before calling Map().
    private static string? Map(SpecialType specialType, bool isUnicode = true) => specialType switch
    {
        SpecialType.System_Boolean => "global::System.Data.DbType.Boolean",
        SpecialType.System_Byte    => "global::System.Data.DbType.Byte",
        SpecialType.System_SByte   => "global::System.Data.DbType.Byte",    // reinterpret: sbyte ↔ byte (unchecked)
        SpecialType.System_Int16   => "global::System.Data.DbType.Int16",
        SpecialType.System_UInt16  => "global::System.Data.DbType.Int16",   // reinterpret: ushort ↔ short (unchecked)
        SpecialType.System_Int32   => "global::System.Data.DbType.Int32",
        SpecialType.System_UInt32  => "global::System.Data.DbType.Int32",   // reinterpret: uint ↔ int (unchecked)
        SpecialType.System_Int64   => "global::System.Data.DbType.Int64",
        SpecialType.System_UInt64  => "global::System.Data.DbType.Int64",   // reinterpret: ulong ↔ long (unchecked)
        SpecialType.System_Single => "global::System.Data.DbType.Single",
        SpecialType.System_Double => "global::System.Data.DbType.Double",
        SpecialType.System_Decimal => "global::System.Data.DbType.Decimal",
        SpecialType.System_String => isUnicode ? "global::System.Data.DbType.String" : "global::System.Data.DbType.AnsiString",
        SpecialType.System_Char => isUnicode ? "global::System.Data.DbType.StringFixedLength" : "global::System.Data.DbType.AnsiStringFixedLength",
        // DateTime2, not DateTime: an explicit DbType is emitted on every mapped parameter even when
        // prepared statements are off, and SqlClient maps DbType.DateTime to the legacy `datetime`
        // SQL type (range 1753+, ~3.33ms precision) which can truncate/throw against modern
        // `datetime2` columns. DbType.DateTime2 round-trips both legacy `datetime` and `datetime2`
        // and is the modern default; other providers (Npgsql/SQLite/MySQL) treat them equivalently.
        SpecialType.System_DateTime => "global::System.Data.DbType.DateTime2",
        _ => null,
    };
}
