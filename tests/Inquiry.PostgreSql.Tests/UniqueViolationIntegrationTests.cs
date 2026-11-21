using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Tests.UniqueViolations;
using Npgsql;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class UniqueViolationIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public UniqueViolationIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        """
        CREATE TABLE "UvParent" ("Id" INTEGER NOT NULL PRIMARY KEY, "Code" VARCHAR(20) NOT NULL);
        CREATE UNIQUE INDEX "IX_UvParent_Code" ON "UvParent" ("Code");
        CREATE TABLE "UvChild" ("Id" INTEGER NOT NULL PRIMARY KEY, "ParentId" INTEGER NOT NULL REFERENCES "UvParent" ("Id"));
        """;

    [SkippableFact]
    public async Task DuplicatePrimaryKeyIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicatePrimaryKeyAsync<PostgresException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [SkippableFact]
    public async Task DuplicateUniqueIndexIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateUniqueIndexAsync<PostgresException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationAsync<PostgresException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [SkippableFact]
    public async Task NotNullViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.NotNullViolationAsync<PostgresException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
    }

    [SkippableFact]
    public async Task DuplicateInBatchIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateInBatchAsync<PostgresException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationInBatchIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationInBatchAsync<PostgresException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }
}
