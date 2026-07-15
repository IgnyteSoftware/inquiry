using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
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

        var sb = new StringBuilder("[");
        var first = true;
        foreach (var value in values)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendValue(sb, value);
        }

        return sb.Append(']').ToString();
    }

    private static void AppendValue<T>(StringBuilder sb, T value)
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
            case byte v: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
            case sbyte v: sb.Append(unchecked((byte)v).ToString(CultureInfo.InvariantCulture)); break;
            case short v: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
            case ushort v: sb.Append(unchecked((short)v).ToString(CultureInfo.InvariantCulture)); break;
            case int v: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
            case uint v: sb.Append(unchecked((int)v).ToString(CultureInfo.InvariantCulture)); break;
            case long v: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
            case ulong v: sb.Append(unchecked((long)v).ToString(CultureInfo.InvariantCulture)); break;
            case float v when float.IsFinite(v): sb.Append(v.ToString("R", CultureInfo.InvariantCulture)); break;
            case double v when double.IsFinite(v): sb.Append(v.ToString("R", CultureInfo.InvariantCulture)); break;
            case float: throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
            case double: throw new System.ArgumentOutOfRangeException(nameof(value), "JSON does not support non-finite floating-point values.");
            case decimal v: sb.Append(v.ToString(CultureInfo.InvariantCulture)); break;
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
