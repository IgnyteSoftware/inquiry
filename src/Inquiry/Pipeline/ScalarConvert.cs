using System;
using System.Globalization;

namespace Inquiry.Pipeline;

/// <summary>
/// Converts the <see cref="System.Data.Common.DbCommand.ExecuteScalar()"/> result to the requested
/// CLR type for scalar aggregates. A null/<see cref="DBNull"/> result (e.g. <c>SUM</c> over no
/// rows) maps to <c>default(T)</c> — which is <see langword="null"/> for a nullable <c>T</c>. Providers
/// return aggregates in dialect-specific types (SQLite returns <c>long</c> for <c>COUNT</c>, etc.), so
/// a non-matching value is coerced via <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/>.
/// </summary>
internal static class ScalarConvert
{
    public static T From<T>(object? value)
    {
        if (value is null || value is DBNull)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (target.IsEnum)
        {
            return (T)Enum.ToObject(target, value);
        }

        return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
