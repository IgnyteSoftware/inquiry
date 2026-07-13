using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class OracleTestHarnessCleanupTests
{
    private readonly OracleContainerFixture _fixture;

    public OracleTestHarnessCleanupTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InvalidDdlDropsTheDisposableSchemaAndPreservesTheSetupFailure()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        string? schemaUser = null;

        var failure = await Assert.ThrowsAsync<OracleException>(() =>
            OracleTestHarness.CreateFromDdlAsync(
                _fixture.AdminConnectionString,
                "THIS IS NOT VALID ORACLE DDL",
                "bad_ddl",
                user => schemaUser = user));

        Assert.NotNull(schemaUser);
        Assert.NotEqual(0, failure.Number);
        Assert.False(await UserExistsAsync(schemaUser!));
    }

    [SkippableFact]
    public async Task CancellationAfterUserCreationDropsTheDisposableSchema()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        string? schemaUser = null;
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OracleTestHarness.CreateFromDdlAsync(
                _fixture.AdminConnectionString,
                "CREATE TABLE CancellationProbe (Id NUMBER(10) PRIMARY KEY)",
                "cancel_setup",
                user =>
                {
                    schemaUser = user;
                    cts.Cancel();
                },
                cts.Token));

        Assert.NotNull(schemaUser);
        Assert.False(await UserExistsAsync(schemaUser!));
    }

    [SkippableFact]
    public async Task OrdinaryTeardownIsIdempotentAndLeavesNoDisposableSchema()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            string.Empty,
            "dispose_twice");
        var schemaUser = harness.SchemaUser;

        await harness.DisposeAsync();
        await harness.DisposeAsync();

        Assert.Null(harness.CleanupFailure);
        Assert.False(await UserExistsAsync(schemaUser));
    }

    private async Task<bool> UserExistsAsync(string schemaUser)
    {
        await using var admin = new OracleConnection(_fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.BindByName = true;
        command.CommandText = "SELECT COUNT(*) FROM ALL_USERS WHERE USERNAME = :username";
        command.Parameters.Add("username", OracleDbType.Varchar2).Value = schemaUser;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) != 0;
    }
}
