using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Tests.HandwrittenQueries;

namespace Inquiry.Oracle.Tests;

/// <summary>Live #620 checks for [InquiryQuery]. The scenarios live in tests/Shared.</summary>
[Collection(OracleCollection.Name)]
public sealed class HandwrittenQueryIntegrationTests
{
    private const string Ddl = "CREATE TABLE query_item (id NUMBER(10) PRIMARY KEY, name VARCHAR2(50) NOT NULL, score NUMBER(10) NOT NULL)";

    private readonly OracleContainerFixture _fixture;
    public HandwrittenQueryIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ReturnShapesBindByName()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "query");
        await HandwrittenQueryScenarios.ReturnShapesBindByNameAsync(harness.GetRequiredService<QueryItemStore>());
    }

    [SkippableFact]
    public async Task CallsJoinTheActiveTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "query");
        await HandwrittenQueryScenarios.CallsJoinTheActiveTransactionAsync(harness.GetRequiredService<IInquiry>(), harness.GetRequiredService<QueryItemStore>());
    }
}
