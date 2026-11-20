using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Tests.UniqueViolations;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class UniqueViolationIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public UniqueViolationIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE UvParent (Id NUMBER(10) NOT NULL PRIMARY KEY, Code VARCHAR2(20) NOT NULL); " +
        "CREATE UNIQUE INDEX IX_UvParent_Code ON UvParent (Code); " +
        "CREATE TABLE UvChild (Id NUMBER(10) NOT NULL PRIMARY KEY, ParentId NUMBER(10) NOT NULL REFERENCES UvParent (Id))";

    [SkippableFact]
    public async Task DuplicatePrimaryKeyIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicatePrimaryKeyAsync<OracleException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(1, exception.Number);
    }

    [SkippableFact]
    public async Task DuplicateUniqueIndexIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateUniqueIndexAsync<OracleException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(1, exception.Number);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationAsync<OracleException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(2291, exception.Number);
    }
}
