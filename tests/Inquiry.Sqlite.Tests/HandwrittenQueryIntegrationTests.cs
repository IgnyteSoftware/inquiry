using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Tests.HandwrittenQueries;

namespace Inquiry.Sqlite.Tests;

/// <summary>Live #620 checks for [InquiryQuery]. The scenarios live in tests/Shared.</summary>
public sealed class HandwrittenQueryIntegrationTests
{
    private const string Ddl = "CREATE TABLE \"query_item\" (\"id\" INTEGER PRIMARY KEY, \"name\" TEXT NOT NULL, \"score\" INTEGER NOT NULL)";

    [Fact]
    public async Task ReturnShapesBindByName()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Query", foreignKeys: false);
        await HandwrittenQueryScenarios.ReturnShapesBindByNameAsync(harness.GetRequiredService<QueryItemStore>());
    }

    [Fact]
    public async Task CallsJoinTheActiveTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Query", foreignKeys: false);
        await HandwrittenQueryScenarios.CallsJoinTheActiveTransactionAsync(harness.GetRequiredService<IInquiry>(), harness.GetRequiredService<QueryItemStore>());
    }
}
