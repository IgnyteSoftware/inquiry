using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Tests.UniqueViolations;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

public sealed class UniqueViolationIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE UvParent (Id INTEGER NOT NULL PRIMARY KEY, Code TEXT NOT NULL); " +
        "CREATE UNIQUE INDEX IX_UvParent_Code ON UvParent (Code); " +
        "CREATE TABLE UvChild (Id INTEGER NOT NULL PRIMARY KEY, ParentId INTEGER NOT NULL REFERENCES UvParent (Id));";

    [Fact]
    public async Task DuplicatePrimaryKeyIsUniqueViolation()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicatePrimaryKeyAsync<SqliteException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(SQLitePCL.raw.SQLITE_CONSTRAINT_PRIMARYKEY, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task DuplicateUniqueIndexIsUniqueViolation()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.DuplicateUniqueIndexAsync<SqliteException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task ForeignKeyViolationIsNotUniqueViolation()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "Uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.ForeignKeyViolationAsync<SqliteException>(harness.GetRequiredService<UvChildStore>());

        Assert.Equal(SQLitePCL.raw.SQLITE_CONSTRAINT_FOREIGNKEY, exception.SqliteExtendedErrorCode);
    }
}
