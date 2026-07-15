using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;

namespace Inquiry.Parameters;

/// <summary>
/// Runtime helper for <c>Compare.In</c> predicates on dialects that bind the whole collection as a
/// single native array parameter (PostgreSQL <c>col = ANY(@name)</c>). The command text stays
/// constant across list lengths — server-side prepared statements remain reusable and the
/// per-element parameter cap does not apply — so this replaces <see cref="InquiryInExpansion"/>'s
/// text rewrite on those dialects.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryArrayParameter
{
    /// <summary>
    /// Binds <paramref name="values"/> as one typed-array parameter named
    /// <paramref name="parameterName"/>. A null or empty collection binds an empty array, which
    /// matches no rows under <c>= ANY</c> — the same semantics as an empty IN list. Enum elements
    /// are coerced to their underlying integral type (matching the scalar binder); null elements
    /// of an enum collection are dropped, which is semantics-preserving because an IN/ANY
    /// comparison never matches NULL.
    /// </summary>
    /// <typeparam name="T">The element type of the IN collection.</typeparam>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
    {
        if (command is null) throw new System.ArgumentNullException(nameof(command));
        if (parameterName is null) throw new System.ArgumentNullException(nameof(parameterName));

        var parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = ToArrayValue(values);
        command.Parameters.Add(parameter);
    }

    internal static object ToArrayValue<T>(IEnumerable<T>? values)
    {
        if (typeof(T) == typeof(sbyte)) return ReinterpretSByte((IEnumerable<sbyte>?)(object?)values);
        if (typeof(T) == typeof(ushort)) return ReinterpretUInt16((IEnumerable<ushort>?)(object?)values);
        if (typeof(T) == typeof(uint)) return ReinterpretUInt32((IEnumerable<uint>?)(object?)values);
        if (typeof(T) == typeof(ulong)) return ReinterpretUInt64((IEnumerable<ulong>?)(object?)values);
        if (typeof(T) == typeof(sbyte?)) return ReinterpretNullableSByte((IEnumerable<sbyte?>?)(object?)values);
        if (typeof(T) == typeof(ushort?)) return ReinterpretNullableUInt16((IEnumerable<ushort?>?)(object?)values);
        if (typeof(T) == typeof(uint?)) return ReinterpretNullableUInt32((IEnumerable<uint?>?)(object?)values);
        if (typeof(T) == typeof(ulong?)) return ReinterpretNullableUInt64((IEnumerable<ulong?>?)(object?)values);

        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (elementType.IsEnum)
        {
            // Build a typed array of the enum's underlying integral type so the provider can
            // infer the array's database type (a boxed-enum array is rejected by Npgsql). The
            // TypeCode switch keeps this AOT-safe; every possible underlying type has a
            // statically instantiated conversion.
            return System.Type.GetTypeCode(System.Enum.GetUnderlyingType(elementType)) switch
            {
                System.TypeCode.SByte => ReinterpretEnumSByte(values),
                System.TypeCode.Byte => ConvertEnumElements<T, byte>(values),
                System.TypeCode.Int16 => ConvertEnumElements<T, short>(values),
                System.TypeCode.UInt16 => ReinterpretEnumUInt16(values),
                System.TypeCode.UInt32 => ReinterpretEnumUInt32(values),
                System.TypeCode.Int64 => ConvertEnumElements<T, long>(values),
                System.TypeCode.UInt64 => ReinterpretEnumUInt64(values),
                _ => ConvertEnumElements<T, int>(values),
            };
        }

        return values switch
        {
            null => System.Array.Empty<T>(),
            T[] array => array,
            _ => values.ToArray(),
        };
    }

    private static short[] ReinterpretSByte(IEnumerable<sbyte>? values) => MapTyped(values, static value => unchecked((short)(byte)value));
    private static short[] ReinterpretUInt16(IEnumerable<ushort>? values) => MapTyped(values, static value => unchecked((short)value));
    private static int[] ReinterpretUInt32(IEnumerable<uint>? values) => MapTyped(values, static value => unchecked((int)value));
    private static long[] ReinterpretUInt64(IEnumerable<ulong>? values) => MapTyped(values, static value => unchecked((long)value));
    private static short?[] ReinterpretNullableSByte(IEnumerable<sbyte?>? values) => MapTyped<sbyte?, short?>(values, static value => value.HasValue ? unchecked((short)(byte)value.Value) : null);
    private static short?[] ReinterpretNullableUInt16(IEnumerable<ushort?>? values) => MapTyped<ushort?, short?>(values, static value => value.HasValue ? unchecked((short)value.Value) : null);
    private static int?[] ReinterpretNullableUInt32(IEnumerable<uint?>? values) => MapTyped<uint?, int?>(values, static value => value.HasValue ? unchecked((int)value.Value) : null);
    private static long?[] ReinterpretNullableUInt64(IEnumerable<ulong?>? values) => MapTyped<ulong?, long?>(values, static value => value.HasValue ? unchecked((long)value.Value) : null);

    private static short[] ReinterpretEnumSByte<T>(IEnumerable<T>? values) => MapNonNull(values, static value => unchecked((short)(byte)System.Convert.ToSByte(value, System.Globalization.CultureInfo.InvariantCulture)));
    private static short[] ReinterpretEnumUInt16<T>(IEnumerable<T>? values) => MapNonNull(values, static value => unchecked((short)System.Convert.ToUInt16(value, System.Globalization.CultureInfo.InvariantCulture)));
    private static int[] ReinterpretEnumUInt32<T>(IEnumerable<T>? values) => MapNonNull(values, static value => unchecked((int)System.Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture)));
    private static long[] ReinterpretEnumUInt64<T>(IEnumerable<T>? values) => MapNonNull(values, static value => unchecked((long)System.Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture)));

    private static TResult[] MapTyped<T, TResult>(IEnumerable<T>? values, System.Func<T, TResult> selector)
    {
        if (values is null) return System.Array.Empty<TResult>();
        if (values is ICollection<T> collection)
        {
            var result = new TResult[collection.Count];
            var index = 0;
            foreach (var value in collection) result[index++] = selector(value);
            return result;
        }

        var list = new List<TResult>();
        foreach (var value in values) list.Add(selector(value));
        return list.ToArray();
    }

    private static TResult[] MapNonNull<T, TResult>(IEnumerable<T>? values, System.Func<T, TResult> selector)
    {
        if (values is null) return System.Array.Empty<TResult>();

        var converted = values is ICollection<T> collection
            ? new List<TResult>(collection.Count)
            : new List<TResult>();
        foreach (var value in values)
        {
            if (value is not null) converted.Add(selector(value));
        }

        return converted.ToArray();
    }

    private static TUnderlying[] ConvertEnumElements<T, TUnderlying>(IEnumerable<T>? values)
        where TUnderlying : struct
    {
        if (values is null)
        {
            return System.Array.Empty<TUnderlying>();
        }

        var converted = new List<TUnderlying>();
        foreach (var value in values)
        {
            if (value is not null)
            {
                converted.Add((TUnderlying)System.Convert.ChangeType(value, typeof(TUnderlying), System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return converted.ToArray();
    }
}
