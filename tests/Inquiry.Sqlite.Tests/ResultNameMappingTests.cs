using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

[InquiryAdHoc]
public sealed class NameMappedPair
{
    public int First { get; set; }

    public int Second { get; set; }

    public string? Label { get; set; }
}

[InquiryAdHoc]
public sealed class NameMappedGuidRow
{
    public Guid Token { get; set; }
}

[InquiryTable("NameMappedGadget")]
public sealed class NameMappedGadget
{
    [InquiryKey("gadget_id")]
    public int Id { get; set; }

    [InquiryColumn("gadget_name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("gadget_rank")]
    public int Rank { get; set; }
}

/// <summary>
/// Handwritten SQL maps result columns to properties by name (#475). Entities match their mapped
/// column names, ad-hoc DTOs match property names, and mapping errors name the property and column.
/// </summary>
public sealed class ResultNameMappingTests
{
    private const string Ddl = """
        CREATE TABLE NameMappedGadget (gadget_id INTEGER PRIMARY KEY, gadget_name TEXT NOT NULL, gadget_rank INTEGER NOT NULL);
        INSERT INTO NameMappedGadget (gadget_id, gadget_name, gadget_rank) VALUES (1, 'alpha', 10), (2, 'beta', 20);
        """;

    [Fact]
    public async Task SameTypeColumnsInOppositeOrderReachTheMatchingProperties()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var list = await inquiry.QueryListAsync<NameMappedPair>($"SELECT 'x' AS Label, 2 AS second, 1 AS FIRST");
        AssertPair(Assert.Single(list));

        AssertPair(await inquiry.QuerySingleOrDefaultAsync<NameMappedPair>($"SELECT 2 AS Second, 1 AS First, 'x' AS Label"));

        await foreach (var pair in inquiry.QueryAsync<NameMappedPair>($"SELECT 2 AS Second, 'x' AS Label, 1 AS First"))
        {
            AssertPair(pair);
        }

        await using var transaction = await inquiry.BeginTransactionAsync();
        AssertPair(Assert.Single(await transaction.QueryListAsync<NameMappedPair>($"SELECT 2 AS Second, 1 AS First, 'x' AS Label")));
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task ExtraColumnsAreIgnored()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var pair = await inquiry.QuerySingleOrDefaultAsync<NameMappedPair>(
            $"SELECT 0 AS Helper, 1 AS First, 'y' AS Other, 2 AS Second, 'x' AS Label, 3 AS Tail");

        AssertPair(pair);
    }

    [Fact]
    public async Task EntitiesMatchTheirMappedColumnNames()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var gadgets = await inquiry.QueryListAsync<NameMappedGadget>(
            $"SELECT gadget_rank, gadget_name, gadget_id FROM NameMappedGadget ORDER BY gadget_id");

        Assert.Collection(
            gadgets,
            gadget => Assert.Equal((1, "alpha", 10), (gadget.Id, gadget.Name, gadget.Rank)),
            gadget => Assert.Equal((2, "beta", 20), (gadget.Id, gadget.Name, gadget.Rank)));

        // A column named after the property does not satisfy a mapped column name.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.QueryListAsync<NameMappedGadget>(
            $"SELECT gadget_id, gadget_name AS Name, gadget_rank FROM NameMappedGadget"));
        Assert.Contains("property 'Name' needs a column named 'gadget_name'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingColumnThrowsBeforeAnyRowAndListsTheAvailableColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        // The result has no rows, so the error comes from the result columns alone.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.QueryListAsync<NameMappedPair>(
            $"SELECT gadget_id AS First, gadget_name AS Label FROM NameMappedGadget WHERE 1 = 0"));

        Assert.Contains(typeof(NameMappedPair).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("property 'Second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Available columns: First, Label", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoColumnsThatMatchOnePropertyThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.QueryListAsync<NameMappedPair>(
            $"SELECT 1 AS First, 3 AS first, 2 AS Second, 'x' AS Label"));

        Assert.Contains("both match property 'First'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Available columns: First, first, Second, Label", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullInNonNullablePropertyNamesTheProperty()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.QueryListAsync<NameMappedPair>(
            $"SELECT 1 AS First, NULL AS Second, 'x' AS Label"));

        Assert.Contains($"property '{typeof(NameMappedPair).FullName}.Second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("the value is NULL and the property type does not allow null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncompatibleColumnTypeNamesThePropertyAndColumn()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "NameMap");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.QueryListAsync<NameMappedGuidRow>(
            $"SELECT 0 AS Helper, 'not a guid' AS token"));

        Assert.Contains("column 'token'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"property '{typeof(NameMappedGuidRow).FullName}.Token'", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.InnerException);
    }

    private static void AssertPair(NameMappedPair? pair)
    {
        Assert.NotNull(pair);
        Assert.Equal(1, pair.First);
        Assert.Equal(2, pair.Second);
        Assert.Equal("x", pair.Label);
    }
}
