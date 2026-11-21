using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Tests.UpdateReturning;

namespace Inquiry.Sqlite.Tests;

/// <summary>Set-based updates that return changed values, read with SQLite's UPDATE ... RETURNING.</summary>
public sealed class UpdateReturningIntegrationTests
{
    private const string Ddl = "CREATE TABLE ReturningCounter (Id INTEGER PRIMARY KEY, Category TEXT NOT NULL, Hits INTEGER NOT NULL);";

    private static Task<SqliteTestHarness> CreateAsync() => SqliteTestHarness.CreateAsync(Ddl, "updreturning");

    [Fact]
    public async Task SingleValueReturnsTheNewValueOrNull()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertSingleValueReturnsTheNewValueOrNullAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [Fact]
    public async Task ListReturnsOneEntryPerUpdatedRow()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertListReturnsOneEntryPerUpdatedRowAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [Fact]
    public async Task RowsShowAChangedFilterColumn()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertRowsShowAChangedFilterColumnAsync(harness.GetRequiredService<ReturningCounterStore>());
    }
}
