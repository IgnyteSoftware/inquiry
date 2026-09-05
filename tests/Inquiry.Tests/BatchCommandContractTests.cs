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
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxParametersPerCommand = 0).ParamName);
    }

    [Fact]
    public void BatchCommandPreservesBindersAndCommandType()
    {
        Action<InquiryParameterTarget, int> row = static (_, _) => { };
        Action<System.Data.Common.DbCommand, IReadOnlyList<int>> chunk = static (_, _) => { };

        var command = new InquiryBatchCommand<int>("work", row, CommandType.StoredProcedure, chunk);

        Assert.Equal("work", command.CommandText);
        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Same(row, command.BindItem);
        Assert.Same(chunk, command.BindChunk);
        Assert.False(command.PreferPrepareOnce);
    }

    [Fact]
    public void BatchCommandPreservesDescriptorPreparationPreference()
    {
        var command = new InquiryBatchCommand<int>(
            "work", static (_, _) => { }, CommandType.Text, bindChunk: null, preferPrepareOnce: true);

        Assert.True(command.PreferPrepareOnce);
    }

    [Fact]
    public void LegacyDefaultLiteralConstructorCallRemainsUnambiguous()
    {
        Assert.Equal(
            "commandType",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new InquiryBatchCommand<int>("work", static (_, _) => { }, default)).ParamName);
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

    [Fact]
    public void WholeChunkDefinitionPreservesLimitsAndComputesEffectiveBound()
    {
        var command = new InquiryBatchCommand<int>(
            static count => "work-" + count,
            static (_, _) => { },
            parametersPerItem: 3,
            maxItemsPerCommand: 10);

        Assert.Null(command.CommandText);
        Assert.Null(command.BindItem);
        Assert.Equal(3, command.ParametersPerItem);
        Assert.Equal(10, command.MaxItemsPerCommand);
        Assert.Equal(10, command.SetBasedMaxItemsPerCommand);
        Assert.Equal(6, command.GetEffectiveChunkSize(maxBatchSize: 20, maxParametersPerCommand: 20));
        Assert.Equal("work-6", command.GetChunkCommandText(6));
    }

    [Fact]
    public void WholeChunkDefinitionValidatesMetadataAndFactoryResult()
    {
        Assert.Equal("parametersPerItem", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InquiryBatchCommand<int>(static _ => "work", static (_, _) => { }, -1)).ParamName);
        Assert.Equal("maxItemsPerCommand", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InquiryBatchCommand<int>(static _ => "work", static (_, _) => { }, 0, 0)).ParamName);
        Assert.Equal("setBasedMaxItemsPerCommand", Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InquiryBatchCommand<int>(
                "row", static (_, _) => { }, static _ => "chunk", static (_, _) => { },
                static _ => true, parametersPerItem: 1, maxItemsPerCommand: 10,
                commandType: CommandType.Text, setBasedMaxItemsPerCommand: 0)).ParamName);

        var command = new InquiryBatchCommand<int>(static _ => " ", static (_, _) => { }, 0);
        Assert.Equal("commandTextFactory", Assert.Throws<ArgumentException>(() => command.GetChunkCommandText(1)).ParamName);
    }

    [Fact]
    public void SelectableDefinitionPreservesExplicitSetBasedLimit()
    {
        var unbounded = new InquiryBatchCommand<int>(
            "row", static (_, _) => { }, static _ => "chunk", static (_, _) => { },
            static _ => true, parametersPerItem: 1, maxItemsPerCommand: 10, commandType: CommandType.Text);
        var bounded = new InquiryBatchCommand<int>(
            "row", static (_, _) => { }, static _ => "chunk", static (_, _) => { },
            static _ => true, parametersPerItem: 1, maxItemsPerCommand: 10,
            commandType: CommandType.Text, setBasedMaxItemsPerCommand: 3);

        Assert.Equal(int.MaxValue, unbounded.SetBasedMaxItemsPerCommand);
        Assert.Equal(3, bounded.SetBasedMaxItemsPerCommand);
    }
}
