using Microsoft.Data.Sqlite;

namespace Inquiry.Sample.Data;

internal static class SampleDatabase
{
    public static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TOrganization (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1 NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TUser (
                [Key] TEXT PRIMARY KEY,
                FirstName TEXT NOT NULL,
                LastName TEXT NOT NULL,
                Email TEXT UNIQUE NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TOrganizationToUser (
                [Key] TEXT PRIMARY KEY,
                TOrganizationKey TEXT NOT NULL,
                TUserKey TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1 NOT NULL,
                FOREIGN KEY (TOrganizationKey) REFERENCES TOrganization([Key]),
                FOREIGN KEY (TUserKey) REFERENCES TUser([Key])
            );

            CREATE TABLE IF NOT EXISTS TCategory (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TProduct (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL,
                Price REAL NOT NULL,
                TCategoryKey TEXT NOT NULL,
                FOREIGN KEY (TCategoryKey) REFERENCES TCategory([Key])
            );
            """;

        await command.ExecuteNonQueryAsync();
    }
}
