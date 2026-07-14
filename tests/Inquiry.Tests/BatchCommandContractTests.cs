using Inquiry.Commands;
using System.Data;

namespace Inquiry.Tests;

public sealed class BatchCommandContractTests
{
    [Fact]
    public void MaxBatchSizeDefaultsToOneThousandAndRejectsNonPositiveValues()
    {
        var options = new InquiryOptions();

        Assert.Equal(1000, InquiryOptions.DefaultMaxBatchSize);
        Assert.Equal(InquiryOptions.DefaultMaxBatchSize, options.MaxBatchSize);
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxBatchSize = 0).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxBatchSize = -1).ParamName);
    }

    [Fact]
    public void BatchCommandPreservesBindersAndCommandType()
    {
        Action<InquiryParameterTarget, int> row = static (_, _) => { };
        Action<InquiryParameterTarget, IReadOnlyList<int>> chunk = static (_, _) => { };

        var command = new InquiryBatchCommand<int>("work", row, CommandType.StoredProcedure, chunk);

        Assert.Equal("work", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Same(row, command.BindItem);
        Assert.Same(chunk, command.BindChunk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BatchCommandRejectsEmptyCommandText(string? commandText)
        => Assert.Equal(
            "commandText",
            Assert.Throws<ArgumentException>(() => new InquiryBatchCommand<int>(commandText!, static (_, _) => { })).ParamName);

    [Fact]
    public void BatchCommandRejectsNullBinderInvalidCommandTypeAndDefaultValue()
    {
        Assert.Equal("bindItem", Assert.Throws<ArgumentNullException>(() => new InquiryBatchCommand<int>("work", null!)).ParamName);
        Assert.Equal(
            "commandType",
            Assert.Throws<ArgumentOutOfRangeException>(() => new InquiryBatchCommand<int>("work", static (_, _) => { }, (CommandType)int.MaxValue)).ParamName);
        Assert.Equal("commandText", Assert.Throws<ArgumentException>(() => default(InquiryBatchCommand<int>).Validate()).ParamName);
    }
}
