using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Tests.HandwrittenQueries;

namespace Inquiry.MariaDb.Tests;

/// <summary>Live #620 checks for [InquiryQuery]. The scenarios live in tests/Shared.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class HandwrittenQueryIntegrationTests
{
    private const string Ddl = "CREATE TABLE `query_item` (`id` INT PRIMARY KEY, `name` VARCHAR(50) NOT NULL, `score` INT NOT NULL)";

    private readonly MariaDbContainerFixture _fixture;
    public HandwrittenQueryIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ReturnShapesBindByName()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "query");
        await HandwrittenQueryScenarios.ReturnShapesBindByNameAsync(harness.GetRequiredService<QueryItemStore>());
    }

    [SkippableFact]
    public async Task CallsJoinTheActiveTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "query");
        await HandwrittenQueryScenarios.CallsJoinTheActiveTransactionAsync(harness.GetRequiredService<IInquiry>(), harness.GetRequiredService<QueryItemStore>());
    }
}
