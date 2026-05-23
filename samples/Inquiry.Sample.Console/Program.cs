using Inquiry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder
        .AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        })
        .SetMinimumLevel(LogLevel.Information);
});

services.AddInquiry(options =>
{
    options.UseSqlite(connection);
    options.Logging.EnableCommandLogging = true;
    options.UseMiddleware<LoggingInquiryMiddleware>();
});

await using var serviceProvider = services.BuildServiceProvider();
using var scope = serviceProvider.CreateScope();

var inquiry = scope.ServiceProvider.GetRequiredService<IInquiryClient>();

await inquiry.ExecuteAsync("""
CREATE TABLE users (
    id INTEGER NOT NULL PRIMARY KEY,
    email TEXT NOT NULL,
    display_name TEXT NULL,
    created_at TEXT NOT NULL,
    version INTEGER NOT NULL
);
""");

var user = new User
{
    Id = 1,
    Email = "john@example.com",
    DisplayName = "John",
    CreatedAt = DateTimeOffset.UtcNow,
    Version = 1
};

await inquiry.InsertAsync(user);

var inserted = await inquiry.FindAsync<User, int>(user.Id);
Console.WriteLine($"Inserted: {inserted?.Id} {inserted?.Email} {inserted?.DisplayName}");

user.DisplayName = "John Smith";
user.Version++;
await inquiry.UpdateAsync(user);

var matchingUsers = await inquiry.SelectAsync<User>(query => query
    .Where("\"email\" LIKE @domain", new { domain = "%@example.com" })
    .OrderBy("\"email\"")
    .Limit(10));

foreach (var match in matchingUsers)
{
    Console.WriteLine($"Selected: {match.Id} {match.Email} {match.DisplayName} v{match.Version}");
}

await inquiry.UpsertAsync(new User
{
    Id = 1,
    Email = "john.updated@example.com",
    DisplayName = "John Updated",
    CreatedAt = user.CreatedAt,
    Version = 3
});

var upserted = await inquiry.FindAsync<User, int>(1);
Console.WriteLine($"Upserted: {upserted?.Id} {upserted?.Email} {upserted?.DisplayName} v{upserted?.Version}");

await inquiry.DeleteAsync(upserted!);
var deleted = await inquiry.FindAsync<User, int>(1);
Console.WriteLine(deleted is null ? "Deleted user 1" : "User 1 still exists");

[InquiryTable("users")]
public sealed class User
{
    [InquiryKey]
    [InquiryColumn("id")]
    public int Id { get; set; }

    [InquiryColumn("email")]
    public string Email { get; set; } = string.Empty;

    [InquiryColumn("display_name")]
    public string? DisplayName { get; set; }

    [InquiryColumn("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [InquiryColumn("version")]
    public int Version { get; set; }
}
