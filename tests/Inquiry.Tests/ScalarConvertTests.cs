using Inquiry.Pipeline;

namespace Inquiry.Tests;

/// <summary>
/// SQLite returns SUM and AVG over a REAL column as a Double. .NET 11 converts Double to Decimal exactly,
/// so the same aggregate gave 21.99 on .NET 10 and 21.98999... on .NET 11. The scalar conversion must give
/// the .NET 10 result on every runtime this suite targets.
/// </summary>
public sealed class ScalarConvertTests
{
    // The expected values are what (decimal)value returns on .NET 10. Text formatting to 15 digits
    // gives a different last digit for the third and fourth values.
    [Theory]
    [InlineData(21.99, "21.99")]
    [InlineData(0.30000000000000004, "0.3")]
    [InlineData(24531336.70824185, "24531336.7082418")]
    [InlineData(8.023925561262166E-05, "0.0000802392556126216")]
    [InlineData(123456789012345678d, "123456789012346000")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(-21.99, "-21.99")]
    [InlineData(-0.0, "0")]
    [InlineData(1e-30, "0")]
    public void DoubleConvertsToDecimalAsNet10Does(double value, string expected)
    {
        var expectedDecimal = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedDecimal, ScalarConvert.From<decimal>(value));
        Assert.Equal(expectedDecimal, ScalarConvert.From<decimal?>(value));
    }

    [Theory]
    [InlineData(21.99f, "21.99")]
    [InlineData(1f / 3, "0.3333333")]
    [InlineData(5e-29f, "0.0000000000000000000000000001")]
    public void SingleConvertsToDecimalAsNet10Does(float value, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ScalarConvert.From<decimal>(value));

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(1e30)]
    [InlineData(7.922816251426434E28)]
    [InlineData(-7.922816251426434E28)]
    public void DoubleOutsideTheDecimalRangeOverflows(double value)
        => Assert.Throws<OverflowException>(() => ScalarConvert.From<decimal>(value));

    [Theory]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    [InlineData(7.9228163E28f)]
    public void SingleOutsideTheDecimalRangeOverflows(float value)
        => Assert.Throws<OverflowException>(() => ScalarConvert.From<decimal>(value));

#if !NET11_0_OR_GREATER
    // On .NET 10 the runtime cast is the reference, so compare with it directly.
    [Fact]
    public void MatchesTheRuntimeCastOnNet10()
    {
        var random = new Random(12345);
        var bytes = new byte[8];
        for (var i = 0; i < 2_000_000; i++)
        {
            random.NextBytes(bytes);
            var value = (i & 1) == 0
                ? BitConverter.ToDouble(bytes)
                : (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-30, 30));
            AssertSame(() => (decimal)value, () => ScalarConvert.ToDecimal(value), value);

            var single = (i & 1) == 0 ? BitConverter.ToSingle(bytes) : (float)value;
            AssertSame(() => (decimal)single, () => ScalarConvert.ToDecimal(single), single);
        }
    }

    private static void AssertSame(Func<decimal> runtime, Func<decimal> converted, double value)
    {
        decimal? expected = null;
        try
        {
            expected = runtime();
        }
        catch (OverflowException)
        {
            // The conversion must overflow for the same input.
        }

        if (expected is null)
        {
            Assert.Throws<OverflowException>(() => converted());
        }
        else if (expected != converted())
        {
            Assert.Fail($"{value:R} converts to {expected} on .NET 10 but ScalarConvert gives {converted()}.");
        }
    }
#endif
}
