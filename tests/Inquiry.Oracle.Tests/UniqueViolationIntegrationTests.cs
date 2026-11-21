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
        "CREATE TABLE UvChild (Id NUMBER(10) NOT NULL PRIMARY KEY, ParentId NUMBER(10) NOT NULL REFERENCES UvParent (Id)); " +
        "CREATE TABLE UvDeferred (Id NUMBER(10) NOT NULL, CONSTRAINT UQ_UvDeferred UNIQUE (Id) DEFERRABLE INITIALLY DEFERRED); " +
        "CREATE TABLE UvDeferredChild (ParentId NUMBER(10) NOT NULL, CONSTRAINT FK_UvDeferredChild FOREIGN KEY (ParentId) REFERENCES UvParent (Id) DEFERRABLE INITIALLY DEFERRED)";

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

    [SkippableFact]
    public async Task NotNullViolationIsNotUniqueViolation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        var exception = await UniqueViolationScenarios.NotNullViolationAsync<OracleException>(harness.GetRequiredService<UvParentStore>());

        Assert.Equal(1400, exception.Number);
    }

    // A deferred constraint fails at commit with ORA-02091, which names the cause only in its message.
    [SkippableTheory]
    [InlineData("INSERT INTO UvDeferred (Id) VALUES (1)", 1, true)]
    [InlineData("INSERT INTO UvDeferredChild (ParentId) VALUES (99)", 2291, false)]
    public async Task DeferredConstraintFailureAtCommitIsClassifiedByItsCause(string insert, int cause, bool isUnique)
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "uv");
        await using var _ = harness;

        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        for (var i = 0; i < (isUnique ? 2 : 1); i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (OracleTransaction)transaction;
            command.CommandText = insert;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<OracleException>(() => transaction.CommitAsync());

        Assert.Equal(2091, exception.Number);
        Assert.Contains($"ORA-{cause:D5}:", exception.Message, StringComparison.Ordinal);
        Assert.Equal(isUnique, exception.IsUniqueViolation());
    }
}
