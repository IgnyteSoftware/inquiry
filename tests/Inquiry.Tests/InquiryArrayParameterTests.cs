using System;
using System.Collections.Generic;
using Inquiry.Parameters;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// <see cref="InquiryArrayParameter"/> value normalization for the PostgreSQL <c>= ANY(@p)</c>
/// path: typed arrays pass through, lazy sequences materialize, null/empty bind an empty array
/// (matches no rows), and enum elements coerce to a typed array of their underlying integral type.
/// </summary>
public sealed class InquiryArrayParameterTests
{
    private enum Color { Red = 1, Green = 2 }

    private enum BigFlag : long { A = 5, B = 6 }

    [Fact]
    public void TypedArrayPassesThroughUnchanged()
    {
        var array = new[] { 1, 2, 3 };
        Assert.Same(array, InquiryArrayParameter.ToArrayValue<int>(array));
    }

    [Fact]
    public void LazySequenceMaterializesToTypedArray()
    {
        var value = InquiryArrayParameter.ToArrayValue<string>(Lazy());
        Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(value));

        static IEnumerable<string> Lazy()
        {
            yield return "a";
            yield return "b";
        }
    }

    [Fact]
    public void NullAndEmptyBindEmptyArray()
    {
        Assert.Empty(Assert.IsType<int[]>(InquiryArrayParameter.ToArrayValue<int>(null)));
        Assert.Empty(Assert.IsType<int[]>(InquiryArrayParameter.ToArrayValue<int>(new List<int>())));
    }

    [Fact]
    public void EnumElementsCoerceToUnderlyingTypedArray()
    {
        var value = InquiryArrayParameter.ToArrayValue<Color>(new[] { Color.Red, Color.Green });
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(value));

        var longValue = InquiryArrayParameter.ToArrayValue<BigFlag>(new[] { BigFlag.B });
        Assert.Equal(new long[] { 6 }, Assert.IsType<long[]>(longValue));
    }

    [Fact]
    public void NullableEnumElementsDropNulls()
    {
        // IN/ANY never matches NULL, so dropping null elements preserves semantics while keeping
        // the array typed for the provider.
        var value = InquiryArrayParameter.ToArrayValue<Color?>(new Color?[] { Color.Red, null, Color.Green });
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(value));
    }
}
