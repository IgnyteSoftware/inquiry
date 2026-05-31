using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed class DbTypeMapperTests
{
    [Theory]
    [InlineData(SpecialType.System_Boolean, "global::System.Data.DbType.Boolean")]
    [InlineData(SpecialType.System_Byte, "global::System.Data.DbType.Byte")]
    [InlineData(SpecialType.System_SByte, "global::System.Data.DbType.SByte")]
    [InlineData(SpecialType.System_Int16, "global::System.Data.DbType.Int16")]
    [InlineData(SpecialType.System_UInt16, "global::System.Data.DbType.UInt16")]
    [InlineData(SpecialType.System_Int32, "global::System.Data.DbType.Int32")]
    [InlineData(SpecialType.System_UInt32, "global::System.Data.DbType.UInt32")]
    [InlineData(SpecialType.System_Int64, "global::System.Data.DbType.Int64")]
    [InlineData(SpecialType.System_UInt64, "global::System.Data.DbType.UInt64")]
    [InlineData(SpecialType.System_Single, "global::System.Data.DbType.Single")]
    [InlineData(SpecialType.System_Double, "global::System.Data.DbType.Double")]
    [InlineData(SpecialType.System_Decimal, "global::System.Data.DbType.Decimal")]
    [InlineData(SpecialType.System_String, "global::System.Data.DbType.String")]
    [InlineData(SpecialType.System_Char, "global::System.Data.DbType.StringFixedLength")]
    [InlineData(SpecialType.System_DateTime, "global::System.Data.DbType.DateTime")]
    public void MapsSpecialTypesToDbType(SpecialType specialType, string expected)
    {
        var type = Type(specialType);
        Assert.Equal(expected, DbTypeMapper.TryGetDbTypeExpression(type));
    }

    [Fact]
    public void MapsGuidToDbTypeGuid()
    {
        var type = new TypeData(
            DisplayName: "global::System.Guid",
            NonNullableDisplayName: "global::System.Guid",
            SpecialType: SpecialType.System_ValueType,
            EnumUnderlyingSpecialType: SpecialType.None,
            IsNullable: false,
            IsValueType: true,
            IsGuid: true,
            IsEnum: false);

        Assert.Equal("global::System.Data.DbType.Guid", DbTypeMapper.TryGetDbTypeExpression(type));
    }

    [Theory]
    [InlineData(SpecialType.System_Int32, "global::System.Data.DbType.Int32")]
    [InlineData(SpecialType.System_Byte, "global::System.Data.DbType.Byte")]
    [InlineData(SpecialType.System_Int64, "global::System.Data.DbType.Int64")]
    public void MapsEnumToUnderlyingDbType(SpecialType underlying, string expected)
    {
        var type = new TypeData(
            DisplayName: "global::Demo.Color",
            NonNullableDisplayName: "global::Demo.Color",
            SpecialType: SpecialType.None,
            EnumUnderlyingSpecialType: underlying,
            IsNullable: false,
            IsValueType: true,
            IsGuid: false,
            IsEnum: true);

        Assert.Equal(expected, DbTypeMapper.TryGetDbTypeExpression(type));
    }

    [Fact]
    public void NullableSpecialTypeMapsLikeUnderlying()
    {
        var type = new TypeData(
            DisplayName: "global::System.Int32?",
            NonNullableDisplayName: "global::System.Int32",
            SpecialType: SpecialType.System_Int32,
            EnumUnderlyingSpecialType: SpecialType.None,
            IsNullable: true,
            IsValueType: true,
            IsGuid: false,
            IsEnum: false);

        Assert.Equal("global::System.Data.DbType.Int32", DbTypeMapper.TryGetDbTypeExpression(type));
    }

    [Fact]
    public void ReturnsNullForUnknownType()
    {
        var type = new TypeData(
            DisplayName: "global::Demo.CustomType",
            NonNullableDisplayName: "global::Demo.CustomType",
            SpecialType: SpecialType.None,
            EnumUnderlyingSpecialType: SpecialType.None,
            IsNullable: false,
            IsValueType: false,
            IsGuid: false,
            IsEnum: false);

        Assert.Null(DbTypeMapper.TryGetDbTypeExpression(type));
    }

    private static TypeData Type(SpecialType specialType) => new(
        DisplayName: "x",
        NonNullableDisplayName: "x",
        SpecialType: specialType,
        EnumUnderlyingSpecialType: SpecialType.None,
        IsNullable: false,
        IsValueType: true,
        IsGuid: false,
        IsEnum: false);
}
