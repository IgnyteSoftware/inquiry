using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

// Drives ContactPatches and schema.sql, both extracted from docs/site/articles/features/partial-updates.md,
// and checks every result that page documents.
const string connectionString = "Data Source=partial-updates-recipe.db";
await Execute(File.ReadAllText("schema.sql"));

var services = new ServiceCollection();
services.AddInquiry(typeof(ContactPatches).Assembly);
services.AddInquirySqlite(connectionString);
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();

await Expect("{}", 7, 0, (false, 0), ("old", "mail", 0));
await Expect("""{"phone": null}""", 7, 0, (true, 1), (null, "mail", 1));
await Expect("""{"phone": "555-0100"}""", 7, 1, (true, 1), ("555-0100", "mail", 2));
await Expect("""{"phone": "stale"}""", 7, 1, (true, 0), ("555-0100", "mail", 2));
await Expect("""{"phone": "other tenant"}""", 8, 2, (true, 0), ("555-0100", "mail", 2));
await Expect("""{"email": null}""", 7, 2, (true, 1), ("555-0100", null, 3));
await Execute("UPDATE Contacts SET IsDeleted = 1 WHERE Id = 1");
await Expect("""{"phone": "archived"}""", 7, 3, (true, 0), ("555-0100", null, 3));
Console.WriteLine("PARTIAL-UPDATES-RECIPE-OK");

async Task Expect(string json, long tenantId, int expectedVersion, (bool, int) result, (string?, string?, long) row)
{
    var actual = await ContactPatches.ApplyAsync(inquiry, 1, tenantId, expectedVersion, json);
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Phone, Email, Version FROM Contacts WHERE Id = 1";
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("The recipe row is missing.");
    var stored = (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2));
    if (actual != result || stored != row)
        throw new InvalidOperationException($"{json}: expected {result} {row}, found {actual} {stored}.");
}

async Task Execute(string sql)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}
