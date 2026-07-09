using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Inquiry.Parameters;

/// <summary>
/// Runtime helper for <c>Compare.In</c> predicates on dialects that support a JSON table-function
/// (<c>json_each</c> on SQLite, <c>JSON_TABLE</c> on Oracle). Serializes the collection as a JSON
/// array string and binds it as a single <see cref="DbType.String"/> parameter — constant command
/// text across all list lengths, prepared-statement reuse, no per-element parameter cap. Enum
/// elements are coerced to their underlying integral type (matching the scalar binder).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryJsonArrayParameter
{
    /// <summary>
    /// Binds <paramref name="values"/> as a JSON array string parameter named
    /// <paramref name="parameterName"/>. A null or empty collection binds <c>"[]"</c>, which
    /// produces zero rows from <c>json_each</c>/<c>JSON_TABLE</c> — matching no rows under
    /// <c>IN (SELECT …)</c>.
    /// </summary>
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

        var elementType = System.Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var enumUnderlyingType = elementType.IsEnum ? System.Enum.GetUnderlyingType(elementType) : null;

        var sb = new StringBuilder("[");
        var first = true;

        foreach (var value in values)
        {
            if (value is null) continue;

            if (!first) sb.Append(',');
            first = false;

            object boxed = value;
            if (enumUnderlyingType is not null)
            {
                boxed = System.Convert.ChangeType(boxed, enumUnderlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }

            boxed = boxed switch
            {
                sbyte v  => (object)unchecked((byte)v),
                ushort v => (object)unchecked((short)v),
                uint v   => (object)unchecked((int)v),
                ulong v  => (object)unchecked((long)v),
                _ => boxed,
            };

            switch (boxed)
            {
                case string s:
                    AppendJsonString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case System.Guid g:
                    sb.Append('"');
                    sb.Append(g.ToString());
                    sb.Append('"');
                    break;
                default:
                    sb.Append(System.Convert.ToString(boxed, System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
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
