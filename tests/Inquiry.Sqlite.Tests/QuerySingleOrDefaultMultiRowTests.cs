using Inquiry.Commands;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Pins the contract for <see cref="IInquiry.QuerySingleOrDefaultAsync{T}"/>: when the query
/// returns more than one row the call must throw <see cref="InvalidOperationException"/>.
///
/// Previously the pipeline asked the provider for <c>CommandBehavior.SingleRow</c>, which gives
/// the driver permission to stop after the first row — silently suppressing the multi-row
/// detection on providers that honour the hint. The fix is to drop <c>SingleRow</c> from the
/// single-row read constants so the second <c>ReadAsync</c> reliably observes the extra row.
/// Uses <see cref="GeneratedItem"/> (a registered entity in this test assembly) so the
/// class-materializer DI lookup succeeds.
/// </summary>
public sealed class QuerySingleOrDefaultMultiRowTests
{
    private static readonly string SeedDdl = Schemas.GeneratedItem + """
        INSERT INTO TGeneratedItem (Id, Name) VALUES (1, 'a');
        INSERT INTO TGeneratedItem (Id, Name) VALUES (2, 'a');
        """;

    [Fact]
    public async Task QuerySingleOrDefaultAsyncThrowsWhenMoreThanOneRowMatches()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(SeedDdl, "SingleOrDefault");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await inquiry.QuerySingleOrDefaultAsync<GeneratedItem>(new InquiryCommand("SELECT Id, Name FROM TGeneratedItem WHERE Name = 'a'")));

        Assert.Contains("multiple rows", ex.Message);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsyncReturnsTheOnlyMatch()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(SeedDdl, "SingleOrDefault");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var row = await inquiry.QuerySingleOrDefaultAsync<GeneratedItem>(new InquiryCommand("SELECT Id, Name FROM TGeneratedItem WHERE Id = 1"));
        Assert.NotNull(row);
        Assert.Equal(1, row!.Id);
        Assert.Equal("a", row.Name);
    }

    [Fact]
    public async Task QuerySingleOrDefaultAsyncReturnsNullWhenNoRowMatches()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(SeedDdl, "SingleOrDefault");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var row = await inquiry.QuerySingleOrDefaultAsync<GeneratedItem>(new InquiryCommand("SELECT Id, Name FROM TGeneratedItem WHERE Id = 99"));
        Assert.Null(row);
    }
}
