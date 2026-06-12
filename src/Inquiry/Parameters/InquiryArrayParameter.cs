using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;

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
        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (elementType.IsEnum)
        {
            // Build a typed array of the enum's underlying integral type so the provider can
            // infer the array's database type (a boxed-enum array is rejected by Npgsql). The
            // TypeCode switch keeps this AOT-safe — no Array.CreateInstance (IL3050); every
            // possible underlying type has a statically instantiated conversion.
            return System.Type.GetTypeCode(System.Enum.GetUnderlyingType(elementType)) switch
            {
                System.TypeCode.SByte => ConvertEnumElements<T, sbyte>(values),
                System.TypeCode.Byte => ConvertEnumElements<T, byte>(values),
                System.TypeCode.Int16 => ConvertEnumElements<T, short>(values),
                System.TypeCode.UInt16 => ConvertEnumElements<T, ushort>(values),
                System.TypeCode.UInt32 => ConvertEnumElements<T, uint>(values),
                System.TypeCode.Int64 => ConvertEnumElements<T, long>(values),
                System.TypeCode.UInt64 => ConvertEnumElements<T, ulong>(values),
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
