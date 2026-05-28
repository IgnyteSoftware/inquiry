using Inquiry.Parameters;
using System.Collections;

namespace Inquiry.Tests;

public sealed class InquiryParameterReaderTests
{
    [Fact]
    public void ReadReturnsEmptyForNull()
    {
        Assert.Empty(InquiryParameterReader.Read(null));
    }

    [Fact]
    public void ReadWrapsSingleInquiryParameter()
    {
        var parameter = new InquiryParameter("Id", 1);

        var result = InquiryParameterReader.Read(parameter);

        Assert.Single(result);
        // InquiryParameter is a value type (readonly struct); compare by value.
        Assert.Equal(parameter, result[0]);
    }

    [Fact]
    public void ReadPreservesIEnumerableOfInquiryParameter()
    {
        var input = new[]
        {
            new InquiryParameter("A", 1),
            new InquiryParameter("B", 2),
        };

        var result = InquiryParameterReader.Read(input);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
        Assert.Equal("B", result[1].Name);
    }

    [Fact]
    public void ReadProjectsDictionaryStringObject()
    {
        var input = new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["Name"] = "Alpha",
            ["Maybe"] = null,
        };

        var result = InquiryParameterReader.Read(input);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, p => p.Name == "Id" && (int)p.Value! == 1);
        Assert.Contains(result, p => p.Name == "Name" && (string)p.Value! == "Alpha");
        Assert.Contains(result, p => p.Name == "Maybe" && p.Value is null);
    }

    [Fact]
    public void ReadProjectsAnonymousObjectViaReflection()
    {
        var result = InquiryParameterReader.Read(new { Id = 1, Name = "Alpha", IsActive = true });

        Assert.Equal(3, result.Count);
        Assert.Contains(result, p => p.Name == "Id" && (int)p.Value! == 1);
        Assert.Contains(result, p => p.Name == "Name" && (string)p.Value! == "Alpha");
        Assert.Contains(result, p => p.Name == "IsActive" && (bool)p.Value! == true);
    }

    [Fact]
    public void ReadProjectsKeyValuePairEnumerable()
    {
        // A custom IEnumerable<KeyValuePair<string, object?>> — covers the IEnumerable<KVP> branch.
        IEnumerable<KeyValuePair<string, object?>> pairs = new List<KeyValuePair<string, object?>>
        {
            new("Id", 7),
            new("Name", "Beta"),
        };

        var result = InquiryParameterReader.Read(pairs);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "Id");
        Assert.Contains(result, p => p.Name == "Name");
    }

    [Fact]
    public void ReadRejectsNonStringDictionaryKey()
    {
        IDictionary input = new Hashtable { [1] = "value" };

        var ex = Assert.Throws<ArgumentException>(() => InquiryParameterReader.Read(input));
        Assert.Contains("string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRejectsNullEntryInEnumerable()
    {
        var input = new object?[] { null };

        Assert.Throws<ArgumentException>(() => InquiryParameterReader.Read(input));
    }

    [Fact]
    public void ReadRejectsEnumerableOfArbitraryObjects()
    {
        // Not InquiryParameter, not a KVP — should throw.
        var input = new object[] { new { Foo = 1 } };

        Assert.Throws<ArgumentException>(() => InquiryParameterReader.Read(input));
    }

    [Theory]
    [InlineData(42)]
    [InlineData("just a string")]
    [InlineData(true)]
    [InlineData(3.14)]
    public void ReadRejectsScalarValues(object scalar)
    {
        Assert.Throws<ArgumentException>(() => InquiryParameterReader.Read(scalar));
    }

    [Fact]
    public void ReadRejectsScalarGuid()
    {
        Assert.Throws<ArgumentException>(() => InquiryParameterReader.Read(Guid.NewGuid()));
    }

    [Fact]
    public void ReadHandlesEmptyAnonymousObject()
    {
        var result = InquiryParameterReader.Read(new { });

        Assert.Empty(result);
    }
}
