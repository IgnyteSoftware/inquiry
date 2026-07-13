using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTestHarnessCleanupTests
{
    private readonly SqlServerContainerFixture _fixture;

    public SqlServerTestHarnessCleanupTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InvalidDdlDropsTheDatabaseCreatedForFailedSetup()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        string? databaseName = null;

        await Assert.ThrowsAsync<SqlException>(() =>
            SqlServerTestHarness.CreateFromDdlAsync(
                _fixture.AdminConnectionString,
                "CREATE TABLE [Broken] ([Id] THIS_TYPE_DOES_NOT_EXIST);",
                "invalidddl",
                databaseCreated: name => databaseName = name));

        Assert.False(string.IsNullOrWhiteSpace(databaseName));
        await using var admin = new SqlConnection(_fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE [name] = @name;";
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = databaseName });

        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
}
