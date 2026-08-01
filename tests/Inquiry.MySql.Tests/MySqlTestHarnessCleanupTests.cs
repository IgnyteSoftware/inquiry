using Inquiry.MySql.Tests.Fixtures;
using MySqlConnector;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class MySqlTestHarnessCleanupTests
{
    private readonly MySqlContainerFixture _fixture;

    public MySqlTestHarnessCleanupTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InvalidDdlDropsTheDatabaseCreatedForFailedSetup()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        string? databaseName = null;

        await Assert.ThrowsAsync<MySqlException>(() =>
            MySqlTestHarness.CreateFromDdlAsync(
                _fixture.AdminConnectionString,
                "CREATE TABLE `Broken` (`Id` THIS_TYPE_DOES_NOT_EXIST);",
                "invalid`ddl",
                databaseCreated: name => databaseName = name));

        Assert.False(string.IsNullOrWhiteSpace(databaseName));
        await using var admin = new MySqlConnection(_fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name;";
        command.Parameters.AddWithValue("@name", databaseName);

        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
}
