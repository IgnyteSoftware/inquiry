using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Globalization;
using Inquiry.Parameters;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Parameters;

/// <summary>Binds an <c>IN</c> collection as one JSON array that generated SQL expands with <c>OPENJSON</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class InquiryJsonInParameter
{
    /// <summary>
    /// Serializes <paramref name="values"/> as a JSON array and adds it as one <c>nvarchar(max)</c> parameter.
    /// </summary>
    /// <param name="command">The command that receives the parameter.</param>
    /// <param name="parameterName">The parameter name in the generated SQL.</param>
    /// <param name="values">The elements; null binds an empty array.</param>
    /// <param name="sqlType">The generator-resolved SQL Server system type of the column, such as <c>datetime2</c>.</param>
    /// <param name="scale">The column's fractional-second scale for temporal types, 0 to 7.</param>
    /// <param name="nullable">False when a null element is an error, as it is for a <c>NOT NULL</c> table type.</param>
    public static void Bind<T>(DbCommand command, string parameterName, IEnumerable<T>? values, string sqlType, int scale, bool nullable)
    {
        ArgumentNullException.ThrowIfNull(command);

        ArgumentNullException.ThrowIfNull(parameterName);

        ArgumentNullException.ThrowIfNull(sqlType);

        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, 7);

        // A fixed nvarchar(max) keeps one sp_executesql signature for every list length.
        command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.NVarChar, -1)
        {
            Value = ToJsonArray(nullable || values is null ? values : RejectNulls(values), sqlType, scale),
        });
    }

    internal static string ToJsonArray<T>(IEnumerable<T>? values, string sqlType, int scale)
    {
        if (values is null)
        {
            return "[]";
        }

        // OPENJSON rounds extra fractional digits, but SqlClient truncates a datetime2, time or
        // datetimeoffset value to the column scale. Pre-formatting temporals keeps the table-type result.
        if (typeof(T) == typeof(DateTime))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<DateTime>)(object)values).Select(value => FormatDateTime(value, sqlType, scale)));
        }

        if (typeof(T) == typeof(DateTime?))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<DateTime?>)(object)values).Select(value => value.HasValue ? FormatDateTime(value.GetValueOrDefault(), sqlType, scale) : null));
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<DateTimeOffset>)(object)values).Select(value => FormatDateTimeOffset(value, scale)));
        }

        if (typeof(T) == typeof(DateTimeOffset?))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<DateTimeOffset?>)(object)values).Select(value => value.HasValue ? FormatDateTimeOffset(value.GetValueOrDefault(), scale) : null));
        }

        if (typeof(T) == typeof(TimeOnly))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<TimeOnly>)(object)values).Select(value => FormatTime(value, scale)));
        }

        if (typeof(T) == typeof(TimeOnly?))
        {
            return InquiryJsonArrayParameter.ToJsonArray(((IEnumerable<TimeOnly?>)(object)values).Select(value => value.HasValue ? FormatTime(value.GetValueOrDefault(), scale) : null));
        }

        return InquiryJsonArrayParameter.ToJsonArray(values);
    }

    private static IEnumerable<T> RejectNulls<T>(IEnumerable<T> values)
    {
        var index = 0;
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new InvalidOperationException($"IN list element at index {index} is null but the column is NOT NULL.");
            }

            index++;
            yield return value;
        }
    }

    private static string FormatDateTime(DateTime value, string sqlType, int scale)
    {
        if (sqlType is "datetime" or "smalldatetime")
        {
            // SqlClient rounds to the datetime 1/300 s grid. The server parses at most three digits.
            return new SqlDateTime(value).Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        return new DateTime(Truncate(value.Ticks, scale), DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    }

    private static string FormatDateTimeOffset(DateTimeOffset value, int scale)
        => new DateTimeOffset(Truncate(value.Ticks, scale), value.Offset).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeOnly value, int scale)
        => new TimeOnly(Truncate(value.Ticks, scale)).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static long Truncate(long ticks, int scale)
    {
        var unit = 1L;
        for (var i = scale; i < 7; i++)
        {
            unit *= 10;
        }

        return ticks - (ticks % unit);
    }
}
