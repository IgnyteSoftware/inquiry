using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Tests.UniqueViolations;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class UniqueViolationIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public UniqueViolationIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE [UvParent] ([Id] INT NOT NULL PRIMARY KEY, [Code] NVARCHAR(20) NOT NULL); " +
        "CREATE UNIQUE INDEX [IX_UvParent_Code] ON [UvParent] ([Code]); " +
        "CREATE TABLE [UvChild] ([Id] INT NOT NULL PRIMARY KEY, [ParentId] INT NOT NULL REFERENCES [UvParent] ([Id]));";

    [SkippableFact]
    public async Task DuplicatePrimaryKeyIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicatePrimaryKeyAsync<SqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(2627, exception.Number);
    }

    [SkippableFact]
    public async Task DuplicateUniqueIndexIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateUniqueIndexAsync<SqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(2601, exception.Number);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationAsync<SqlException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(547, exception.Number);
    }
}
