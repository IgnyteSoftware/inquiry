using Inquiry.SqlServer.Parameters;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Inquiry.SqlServer.Tests;

public sealed class InquiryTvpParameterTests
{
    [Fact]
    public void UnsupportedBinderFailsWithoutOpeningConnection()
    {
        using var command = new SqlCommand();

        var exception = Assert.Throws<NotSupportedException>(() =>
            InquiryTvpParameter.BindUnsupported(command, "@values", new[] { DateOnly.MinValue }));

        Assert.Contains(typeof(DateOnly).FullName!, exception.Message);
        Assert.Null(command.Connection);
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Fact]
    public void BindUsesExplicitQualifiedTypeWithoutConnectionIo()
    {
        using var command = new SqlCommand();

        InquiryTvpParameter.Bind(command, "@ids", new[] { 1, 2 }, "[tenant].[Inquiry_Tvp_test]");

        Assert.Null(command.Connection);
        var parameter = Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>()));
        Assert.Equal("@ids", parameter.ParameterName);
        Assert.Equal(SqlDbType.Structured, parameter.SqlDbType);
        Assert.Equal("[tenant].[Inquiry_Tvp_test]", parameter.TypeName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unqualified")]
    [InlineData("dbo.type.extra")]
    [InlineData("dbo.[type]")]
    [InlineData("[dbo].[type].")]
    [InlineData("[dbo].[type")]
    [InlineData("[].[]")]
    public void BindRejectsInvalidTypeName(string? typeName)
    {
        using var command = new SqlCommand();
        Assert.ThrowsAny<ArgumentException>(() =>
            InquiryTvpParameter.Bind(command, "@ids", Array.Empty<int>(), typeName!));
        Assert.Empty(command.Parameters.Cast<SqlParameter>());
    }

    [Theory]
    [InlineData("[schema with spaces].[Inquiry_Tvp_test]")]
    [InlineData("[9.leading].[Inquiry_Tvp_test]")]
    [InlineData("[schema.with.dot].[Inquiry_Tvp_test]")]
    [InlineData("[schema's].[Inquiry_Tvp_test]")]
    [InlineData("[schema]]name].[Inquiry_Tvp_test]")]
    public void BindAcceptsBracketEscapedGeneratedTypeNames(string typeName)
    {
        using var command = new SqlCommand();
        InquiryTvpParameter.Bind(command, "@ids", new[] { 1 }, typeName);
        Assert.Equal(typeName, Assert.IsType<SqlParameter>(Assert.Single(command.Parameters.Cast<SqlParameter>())).TypeName);
    }

    [Fact]
    public void BindRetainsNullAndEmptyCollectionSemantics()
    {
        using var nullCommand = new SqlCommand();
        InquiryTvpParameter.Bind<int>(nullCommand, "@ids", null, "[dbo].[Inquiry_Tvp_test]");
        Assert.Null(Assert.IsType<SqlParameter>(Assert.Single(nullCommand.Parameters.Cast<SqlParameter>())).Value);

        using var emptyCommand = new SqlCommand();
        InquiryTvpParameter.Bind(emptyCommand, "@ids", Array.Empty<int>(), "[dbo].[Inquiry_Tvp_test]");
        Assert.Null(Assert.IsType<SqlParameter>(Assert.Single(emptyCommand.Parameters.Cast<SqlParameter>())).Value);
    }
}
