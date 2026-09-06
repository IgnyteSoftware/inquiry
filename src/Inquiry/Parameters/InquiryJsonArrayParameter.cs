using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Inquiry.Parameters;

/// <summary>Binds a collection as one JSON-array string parameter for SQL JSON table functions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryJsonArrayParameter
{
    /// <summary>Serializes <paramref name="values"/> as JSON and adds one string parameter.</summary>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values)
    {
        if (command is null) throw new System.ArgumentNullException(nameof(command));
        if (parameterName is null) throw new System.ArgumentNullException(nameof(parameterName));

        var parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.DbType = DbType.String;
        parameter.Value = ToJsonArray(values);
        command.Parameters.Add(parameter);
    }

    internal static string ToJsonArray<T>(IEnumerable<T>? values)
    {
        if (values is null) return "[]";

        var sb = new StringBuilder(GetInitialCapacity(values));
        sb.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (!first) sb.Append(',');
            first = false;
            ElementWriter<T>.Append(sb, value);
        }

        return sb.Append(']').ToString();
    }

    private static int GetInitialCapacity<T>(IEnumerable<T> values)
    {
        var estimatedElementLength = ElementMetadata<T>.EstimatedJsonLength;
        if (estimatedElementLength == 0 || !System.Linq.Enumerable.TryGetNonEnumeratedCount(values, out var count))
        {
            return 16;
        }

        var estimatedCapacity = 2L + ((long)estimatedElementLength + 1) * count;
        return estimatedCapacity <= int.MaxValue ? (int)estimatedCapacity : int.MaxValue;
    }

    private static void AppendBoxedValue<T>(StringBuilder sb, T value)
    {
        if (value is null)
        {
            sb.Append("null");
            return;
        }

        object boxed = value;
        var type = ElementMetadata<T>.Type;
        if (ElementMetadata<T>.IsEnum)
        {
            boxed = EnumStorageValue(boxed, ElementMetadata<T>.EnumUnderlyingType!);
        }

        switch (boxed)
        {
            case string s: AppendJsonString(sb, s); break;
            case char c: AppendJsonString(sb, c.ToString()); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case byte v: AppendFormatted(sb, v); break;
            case sbyte v: AppendFormatted(sb, unchecked((byte)v)); break;
            case short v: AppendFormatted(sb, v); break;
            case ushort v: AppendFormatted(sb, unchecked((short)v)); break;
            case int v: AppendFormatted(sb, v); break;
            case uint v: AppendFormatted(sb, unchecked((int)v)); break;
            case long v: AppendFormatted(sb, v); break;
            case ulong v: AppendFormatted(sb, unchecked((long)v)); break;
            case float v when float.IsFinite(v): AppendFormatted(sb, v, "R"); break;
            case double v when double.IsFinite(v): AppendFormatted(sb, v, "R"); break;
            case float: throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
            case double: throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
            case decimal v: AppendFormatted(sb, v); break;
            case System.Guid v: AppendJsonString(sb, v.ToString("D")); break;
            case System.DateTime v: AppendJsonString(sb, v.ToString("O", CultureInfo.InvariantCulture)); break;
            case System.DateTimeOffset v: AppendJsonString(sb, v.ToString("O", CultureInfo.InvariantCulture)); break;
            case System.DateOnly v: AppendJsonString(sb, v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
            case System.TimeOnly v: AppendJsonString(sb, v.ToString("O", CultureInfo.InvariantCulture)); break;
            case byte[] v: AppendJsonString(sb, System.Convert.ToBase64String(v)); break;
            default: throw new System.NotSupportedException($"JSON array binding does not support element type '{type}'.");
        }
    }

    private static object EnumStorageValue(object value, System.Type underlyingType)
        => System.Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.SByte => unchecked((byte)System.Convert.ToSByte(value, CultureInfo.InvariantCulture)),
            TypeCode.Byte => System.Convert.ToByte(value, CultureInfo.InvariantCulture),
            TypeCode.Int16 => System.Convert.ToInt16(value, CultureInfo.InvariantCulture),
            TypeCode.UInt16 => unchecked((short)System.Convert.ToUInt16(value, CultureInfo.InvariantCulture)),
            TypeCode.Int32 => System.Convert.ToInt32(value, CultureInfo.InvariantCulture),
            TypeCode.UInt32 => unchecked((int)System.Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            TypeCode.Int64 => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
            TypeCode.UInt64 => unchecked((long)System.Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            _ => throw new System.NotSupportedException($"Enum underlying type '{underlyingType}' is not supported."),
        };

    // Reflection is paid once per closed T, never once per element. The cached facts are trim/AOT safe:
    // no members are discovered or invoked dynamically.
    private static class ElementMetadata<T>
    {
        internal static readonly System.Type Type = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        internal static readonly bool IsEnum = Type.IsEnum;
        internal static readonly System.Type? EnumUnderlyingType = IsEnum ? System.Enum.GetUnderlyingType(Type) : null;
        internal static readonly int EstimatedJsonLength = GetEstimatedJsonLength(Type);

        private static int GetEstimatedJsonLength(System.Type type)
        {
            // Common widths avoid both repeated growth and allocating every value at its maximum width.
            if (type == typeof(bool)) return 5;
            if (type == typeof(byte) || type == typeof(sbyte)) return 3;
            if (type == typeof(short) || type == typeof(ushort)) return 4;
            if (type == typeof(int) || type == typeof(uint)) return 6;
            if (type == typeof(long) || type == typeof(ulong)) return 10;
            if (type == typeof(float)) return 8;
            if (type == typeof(double) || type == typeof(decimal)) return 12;
            if (type == typeof(char)) return 8;
            if (type == typeof(System.Guid)) return 38;
            if (type == typeof(System.DateTime) || type == typeof(System.DateTimeOffset)) return 35;
            if (type == typeof(System.DateOnly)) return 12;
            if (type == typeof(System.TimeOnly)) return 18;
            if (type.IsEnum) return GetEstimatedJsonLength(System.Enum.GetUnderlyingType(type));
            return 0;
        }
    }

    private static class ElementWriter<T>
    {
        internal static readonly System.Action<StringBuilder, T> Append = Create();

        private static System.Action<StringBuilder, T> Create()
        {
            var type = typeof(T);
            if (type == typeof(string)) return static (sb, value) => AppendString(sb, Unsafe.As<T, string?>(ref value));
            if (type == typeof(byte)) return static (sb, value) => AppendFormatted(sb, Unsafe.As<T, byte>(ref value));
            if (type == typeof(sbyte)) return static (sb, value) => AppendFormatted(sb, unchecked((byte)Unsafe.As<T, sbyte>(ref value)));
            if (type == typeof(short)) return static (sb, value) => AppendFormatted(sb, Unsafe.As<T, short>(ref value));
            if (type == typeof(ushort)) return static (sb, value) => AppendFormatted(sb, unchecked((short)Unsafe.As<T, ushort>(ref value)));
            if (type == typeof(int)) return static (sb, value) => AppendFormatted(sb, Unsafe.As<T, int>(ref value));
            if (type == typeof(uint)) return static (sb, value) => AppendFormatted(sb, unchecked((int)Unsafe.As<T, uint>(ref value)));
            if (type == typeof(long)) return static (sb, value) => AppendFormatted(sb, Unsafe.As<T, long>(ref value));
            if (type == typeof(ulong)) return static (sb, value) => AppendFormatted(sb, unchecked((long)Unsafe.As<T, ulong>(ref value)));
            if (type == typeof(float)) return static (sb, value) => AppendFinite(sb, Unsafe.As<T, float>(ref value));
            if (type == typeof(double)) return static (sb, value) => AppendFinite(sb, Unsafe.As<T, double>(ref value));
            if (type == typeof(decimal)) return static (sb, value) => AppendFormatted(sb, Unsafe.As<T, decimal>(ref value));
            if (type == typeof(byte?)) return static (sb, value) => AppendNullable(sb, Unsafe.As<T, byte?>(ref value));
            if (type == typeof(sbyte?)) return static (sb, value) => AppendNullableSByte(sb, Unsafe.As<T, sbyte?>(ref value));
            if (type == typeof(short?)) return static (sb, value) => AppendNullable(sb, Unsafe.As<T, short?>(ref value));
            if (type == typeof(ushort?)) return static (sb, value) => AppendNullableUInt16(sb, Unsafe.As<T, ushort?>(ref value));
            if (type == typeof(int?)) return static (sb, value) => AppendNullable(sb, Unsafe.As<T, int?>(ref value));
            if (type == typeof(uint?)) return static (sb, value) => AppendNullableUInt32(sb, Unsafe.As<T, uint?>(ref value));
            if (type == typeof(long?)) return static (sb, value) => AppendNullable(sb, Unsafe.As<T, long?>(ref value));
            if (type == typeof(ulong?)) return static (sb, value) => AppendNullableUInt64(sb, Unsafe.As<T, ulong?>(ref value));
            if (type == typeof(float?)) return static (sb, value) => AppendNullableFinite(sb, Unsafe.As<T, float?>(ref value));
            if (type == typeof(double?)) return static (sb, value) => AppendNullableFinite(sb, Unsafe.As<T, double?>(ref value));
            if (type == typeof(decimal?)) return static (sb, value) => AppendNullable(sb, Unsafe.As<T, decimal?>(ref value));
            return AppendBoxedValue;
        }
    }

    private static void AppendString(StringBuilder sb, string? value)
    {
        if (value is null) sb.Append("null");
        else AppendJsonString(sb, value);
    }

    private static void AppendFormatted<T>(StringBuilder sb, T value, ReadOnlySpan<char> format = default)
        where T : System.ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];
        if (!value.TryFormat(buffer, out var charsWritten, format, CultureInfo.InvariantCulture))
        {
            throw new System.InvalidOperationException($"The JSON value '{typeof(T)}' exceeded the formatting buffer.");
        }

        sb.Append(buffer[..charsWritten]);
    }

    private static void AppendFinite(StringBuilder sb, float value)
    {
        if (!float.IsFinite(value)) throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
        AppendFormatted(sb, value, "R");
    }

    private static void AppendFinite(StringBuilder sb, double value)
    {
        if (!double.IsFinite(value)) throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
        AppendFormatted(sb, value, "R");
    }

    private static void AppendNullable<T>(StringBuilder sb, T? value)
        where T : struct, System.ISpanFormattable
    {
        if (value.HasValue) AppendFormatted(sb, value.GetValueOrDefault());
        else sb.Append("null");
    }

    private static void AppendNullableSByte(StringBuilder sb, sbyte? value)
    {
        if (value.HasValue) AppendFormatted(sb, unchecked((byte)value.GetValueOrDefault()));
        else sb.Append("null");
    }

    private static void AppendNullableUInt16(StringBuilder sb, ushort? value)
    {
        if (value.HasValue) AppendFormatted(sb, unchecked((short)value.GetValueOrDefault()));
        else sb.Append("null");
    }

    private static void AppendNullableUInt32(StringBuilder sb, uint? value)
    {
        if (value.HasValue) AppendFormatted(sb, unchecked((int)value.GetValueOrDefault()));
        else sb.Append("null");
    }

    private static void AppendNullableUInt64(StringBuilder sb, ulong? value)
    {
        if (value.HasValue) AppendFormatted(sb, unchecked((long)value.GetValueOrDefault()));
        else sb.Append("null");
    }

    private static void AppendNullableFinite(StringBuilder sb, float? value)
    {
        if (value.HasValue) AppendFinite(sb, value.GetValueOrDefault());
        else sb.Append("null");
    }

    private static void AppendNullableFinite(StringBuilder sb, double? value)
    {
        if (value.HasValue) AppendFinite(sb, value.GetValueOrDefault());
        else sb.Append("null");
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ' || char.IsSurrogate(c))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
