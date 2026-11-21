using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Tests.UniqueViolations;
using MySqlConnector;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class UniqueViolationIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;

    public UniqueViolationIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE `UvParent` (`Id` INT NOT NULL PRIMARY KEY, `Code` VARCHAR(20) NOT NULL); " +
        "CREATE UNIQUE INDEX `IX_UvParent_Code` ON `UvParent` (`Code`); " +
        "CREATE TABLE `UvChild` (`Id` INT NOT NULL PRIMARY KEY, `ParentId` INT NOT NULL, FOREIGN KEY (`ParentId`) REFERENCES `UvParent` (`Id`));";

    [SkippableFact]
    public async Task DuplicatePrimaryKeyIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicatePrimaryKeyAsync<MySqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [SkippableFact]
    public async Task DuplicateUniqueIndexIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateUniqueIndexAsync<MySqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationAsync<MySqlException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(MySqlErrorCode.NoReferencedRow2, exception.ErrorCode);
    }

    [SkippableFact]
    public async Task NotNullViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.NotNullViolationAsync<MySqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(MySqlErrorCode.ColumnCannotBeNull, exception.ErrorCode);
    }

    [SkippableFact]
    public async Task DuplicateInBatchIsUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateInBatchAsync<MySqlException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, exception.ErrorCode);
    }

    [SkippableFact]
    public async Task ForeignKeyViolationInBatchIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationInBatchAsync<MySqlException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(MySqlErrorCode.NoReferencedRow2, exception.ErrorCode);
    }
}
