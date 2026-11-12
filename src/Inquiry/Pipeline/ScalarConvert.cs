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

        if (target == typeof(decimal))
        {
            // .NET 11 converts Double and Single to Decimal exactly, so SUM over a SQLite REAL column
            // gave 21.99 on .NET 10 and 21.98999... on .NET 11. These keep the .NET 10 result on
            // every runtime.
            if (value is double doubleValue)
            {
                return (T)(object)ToDecimal(doubleValue);
            }

            if (value is float singleValue)
            {
                return (T)(object)ToDecimal(singleValue);
            }
        }

        return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private static readonly double[] _doublePowersOf10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14,
        1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28,
    ];

    private static readonly decimal[] _decimalPowersOf10 =
    [
        1e0m, 1e1m, 1e2m, 1e3m, 1e4m, 1e5m, 1e6m, 1e7m, 1e8m, 1e9m, 1e10m, 1e11m, 1e12m, 1e13m, 1e14m,
        1e15m, 1e16m, 1e17m, 1e18m, 1e19m, 1e20m, 1e21m, 1e22m, 1e23m, 1e24m, 1e25m, 1e26m, 1e27m, 1e28m,
    ];

    /// <summary>Converts as .NET 10 does: the value rounded to 15 significant digits.</summary>
    internal static decimal ToDecimal(double value)
        => ToDecimal(value, (int)((BitConverter.DoubleToUInt64Bits(value) >> 52) & 0x7FF) - 1022, digits: 14, 1e15, 1e14);

    /// <summary>Converts as .NET 10 does: the value rounded to 7 significant digits.</summary>
    internal static decimal ToDecimal(float value)
        => ToDecimal(value, (int)((BitConverter.SingleToUInt32Bits(value) >> 23) & 0xFF) - 126, digits: 6, 1e7, 1e6);

    // The .NET 10 runtime algorithm (VarDecFromR8 and VarDecFromR4). It scales the value to an
    // integer of the given number of digits in binary floating point, rounds half to even, and
    // applies the power of ten exactly. Text formatting rounds differently for some values.
    private static decimal ToDecimal(double value, int binaryExponent, int digits, double upperBound, double lowerBound)
    {
        if (binaryExponent < -94)
        {
            return 0m;
        }

        // Also true for NaN and the infinities, whose exponent is the maximum.
        if (binaryExponent > 96)
        {
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        var magnitude = Math.Abs(value);

        // 19728 / 65536 approximates log10(2), so this is the scale that leaves `digits + 1` digits.
        var power = digits - ((binaryExponent * 19728) >> 16);
        if (power >= 0)
        {
            power = Math.Min(power, 28);
            magnitude *= _doublePowersOf10[power];
        }
        else if (power != -1 || magnitude >= upperBound)
        {
            magnitude /= _doublePowersOf10[-power];
        }
        else
        {
            power = 0;
        }

        if (magnitude < lowerBound && power < 28)
        {
            magnitude *= 10;
            power++;
        }

        var mantissa = (ulong)(long)Math.Round(magnitude);
        if (mantissa == 0)
        {
            return 0m;
        }

        var result = power < 0 ? mantissa * _decimalPowersOf10[-power] : mantissa / _decimalPowersOf10[power];
        return value < 0 ? -result : result;
    }
}
