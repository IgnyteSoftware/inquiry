using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Inquiry.Parameters;

namespace Inquiry.Tests;

public sealed class InquiryJsonArrayParameterTests
{
    private enum UnsignedCode : uint { Max = uint.MaxValue }

    [Fact]
    public void NullAndEmptyCollectionsProduceEmptyArray()
    {
        Assert.Equal("[]", InquiryJsonArrayParameter.ToJsonArray<int>(null));
        Assert.Equal("[]", InquiryJsonArrayParameter.ToJsonArray(Array.Empty<int>()));
    }

    [Fact]
    public void NullElementsArePreservedAndStringsAreFullyEscaped()
    {
        var json = InquiryJsonArrayParameter.ToJsonArray<string?>(new[] { null, "a\"\\\b\f\n\r\t\u0001", "\ud800" });
        Assert.Equal("[null,\"a\\\"\\\\\\b\\f\\n\\r\\t\\u0001\",\"\\ud800\"]", json);
    }

    [Fact]
    public void NumericAndEnumValuesUseInvariantSignedStorageRepresentation()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("[-1,1.25,2.5]", InquiryJsonArrayParameter.ToJsonArray<object>(new object[] { -1, 1.25f, 2.5m }));
            Assert.Equal("[-1]", InquiryJsonArrayParameter.ToJsonArray(new[] { UnsignedCode.Max }));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GuidTemporalAndBinaryValuesAreDeterministicStrings()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Assert.Equal("[\"00112233-4455-6677-8899-aabbccddeeff\"]", InquiryJsonArrayParameter.ToJsonArray(new[] { guid }));
        Assert.Equal("[\"2026-07-10\"]", InquiryJsonArrayParameter.ToJsonArray(new[] { new DateOnly(2026, 7, 10) }));
        Assert.Equal("[\"AQID\"]", InquiryJsonArrayParameter.ToJsonArray(new[] { new byte[] { 1, 2, 3 } }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteFloatingPointValuesFail(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => InquiryJsonArrayParameter.ToJsonArray(new[] { value }));

    [Fact]
    public void EnumerableIsConsumedExactlyOnce()
    {
        var values = new SingleUseEnumerable<int>(new[] { 1, 2, 3 });
        Assert.Equal("[1,2,3]", InquiryJsonArrayParameter.ToJsonArray(values));
        Assert.Equal(1, values.EnumerationCount);
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            if (++EnumerationCount != 1) throw new InvalidOperationException("enumerated twice");
            return values.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
