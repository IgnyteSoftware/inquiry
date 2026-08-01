using Inquiry.Converters;

namespace Inquiry.Tests;

public sealed class InquiryJsonConverterTests
{
    [Fact]
    public void RoundTripsThroughJsonText()
    {
        var converter = new InquiryJsonConverter<Payload>();

        var text = converter.ToProvider(new Payload { Name = "inquiry" });

        Assert.Equal("inquiry", converter.FromProvider(text).Name);
    }

    [Fact]
    public void FromProviderThrowsWhenStoredTextDeserializesToNull()
    {
        var converter = new InquiryJsonConverter<Payload>();

        var failure = Assert.Throws<InvalidOperationException>(() => converter.FromProvider("null"));

        Assert.Contains(typeof(Payload).FullName!, failure.Message, StringComparison.Ordinal);
    }

    private sealed class Payload
    {
        public string Name { get; set; } = string.Empty;
    }
}
