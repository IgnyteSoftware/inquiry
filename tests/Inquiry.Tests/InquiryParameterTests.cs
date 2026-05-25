using Inquiry.Parameters;
using System.Data;

namespace Inquiry.Tests;

public sealed class InquiryParameterTests
{
    [Fact]
    public void ConstructorStoresAllProperties()
    {
        var parameter = new InquiryParameter(
            "@Id",
            42,
            DbType.Int32,
            ParameterDirection.InputOutput,
            size: 4,
            precision: 9,
            scale: 0);

        Assert.Equal("@Id", parameter.Name);
        Assert.Equal(42, parameter.Value);
        Assert.Equal(DbType.Int32, parameter.DbType);
        Assert.Equal(ParameterDirection.InputOutput, parameter.Direction);
        Assert.Equal(4, parameter.Size);
        Assert.Equal((byte)9, parameter.Precision);
        Assert.Equal((byte)0, parameter.Scale);
    }

    [Fact]
    public void ConstructorLeavesOptionalsUnsetByDefault()
    {
        var parameter = new InquiryParameter("Name", "Alpha");

        Assert.Equal("Name", parameter.Name);
        Assert.Equal("Alpha", parameter.Value);
        Assert.Null(parameter.DbType);
        Assert.Null(parameter.Direction);
        Assert.Null(parameter.Size);
        Assert.Null(parameter.Precision);
        Assert.Null(parameter.Scale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsBlankName(string? name)
    {
        Assert.Throws<ArgumentException>(() => new InquiryParameter(name!, "value"));
    }

    [Fact]
    public void ValueCanBeNull()
    {
        var parameter = new InquiryParameter("Optional", null);

        Assert.Null(parameter.Value);
    }
}
