using Microsoft.Data.Sqlite;

namespace Inquiry.Sample.Data;

internal static class SampleDatabase
{
    public static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TOrganization (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1 NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }
}
