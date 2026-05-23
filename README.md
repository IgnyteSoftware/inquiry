# Inquiry

Inquiry is a lightweight .NET 6+ database access library for explicit SQL-friendly CRUD workflows.

The initial implementation includes:

- attribute-based table, column, key, ignore, concurrency, audit, and convenience mapping attributes
- reflection metadata fallback with cached compiled property accessors
- source-generator project that emits entity descriptors and a generated registration extension
- provider/dialect abstractions in the core package
- separate SQLite, PostgreSQL, and SQL Server provider packages
- `IInquiryClient` CRUD APIs for find, select, insert, update, delete, upsert, raw query, raw execute, and transactions
- simple query builder with raw SQL fragments plus parameter binding
- middleware pipeline and optional OpenTelemetry `ActivitySource`/`Meter`
- optional Microsoft.Extensions dependency injection and logging integration
- focused xUnit coverage for mapping, SQL generation, client execution, and middleware ordering

## Packages

- `Inquiry`: slim runtime package with the microORM client, mapping, metadata, query builder, command execution, provider abstractions, middleware pipeline, and diagnostics
- `Inquiry.Generators`: source generator package for generated entity descriptors
- `Inquiry.Sqlite`: SQLite dialect and `UseSqlite(...)` registration extensions
- `Inquiry.PostgreSql`: PostgreSQL dialect and `UsePostgreSql(...)` registration extensions
- `Inquiry.SqlServer`: SQL Server dialect and `UseSqlServer(...)` registration extensions
- `Inquiry.Extensions.DependencyInjection`: `services.AddInquiry(...)` and `LoggingInquiryMiddleware`

The core `Inquiry` package intentionally has no concrete providers and no Microsoft.Extensions package dependencies.

## Basic Usage

```csharp
[InquiryTable("users", Schema = "public")]
public sealed partial class User
{
    [InquiryKey]
    [InquiryColumn("id")]
    public Guid Id { get; set; }

    [InquiryColumn("email")]
    public string Email { get; set; } = string.Empty;

    [InquiryColumn("display_name")]
    public string? DisplayName { get; set; }
}
```

```csharp
var client = InquiryClient.Create(connection, InquiryPostgreSqlProvider.Instance);

var user = await client.FindAsync<User, Guid>(id);

var users = await client.SelectAsync<User>(query => query
    .Where("\"email\" = @email", new { email = "name@example.com" })
    .OrderBy("\"email\"")
    .Limit(25));

await client.InsertAsync(user);
await client.UpdateAsync(user);
await client.DeleteAsync(user);
```

## Dependency Injection

Reference `Inquiry.Extensions.DependencyInjection` plus the provider package you need.

```csharp
services.AddInquiry(options =>
{
    options.UsePostgreSql(() => new NpgsqlConnection(connectionString));
    options.UseMiddleware<LoggingInquiryMiddleware>();
    options.UseOpenTelemetry();
});
```

Provider-specific database driver packages are intentionally not referenced by Inquiry. Pass any `DbConnection` implementation from your application.

## Stored Procedures

Stored procedure calls use the same execution path as SQL text, so middleware, logging, OpenTelemetry, transactions, parameter binding, and materialization still apply.

```csharp
var output = InquiryParameter.Output("total", DbType.Int32);

await inquiry.ExecuteStoredProcedureAsync(
    "dbo.CountUsers",
    new[]
    {
        InquiryParameter.Input("domain", "example.com"),
        output
    });

Console.WriteLine($"Total users: {output.Value}");
```

```csharp
await inquiry.ExecuteInTransactionAsync(async (session, cancellationToken) =>
{
    await session.ExecuteStoredProcedureAsync(
        "dbo.ActivateUser",
        new { id = userId },
        cancellationToken);
});
```

```csharp
var users = await inquiry.QueryStoredProcedureAsync<User>(
    "dbo.SearchUsers",
    new { domain = "example.com" });
```

## Projects

- `src/Inquiry`: runtime package
- `src/Inquiry.Generators`: source generator package
- `src/Inquiry.Sqlite`: SQLite provider package
- `src/Inquiry.PostgreSql`: PostgreSQL provider package
- `src/Inquiry.SqlServer`: SQL Server provider package
- `src/Inquiry.Extensions.DependencyInjection`: Microsoft.Extensions integration package
- `tests/Inquiry.Tests`: unit tests with a fake ADO.NET provider
- `benchmarks/Inquiry.Benchmarks`: BenchmarkDotNet baseline for metadata and SQL generation
- `samples/Inquiry.Sample.Console`: runnable DI sample backed by in-memory SQLite

## Run The Sample

```powershell
dotnet run --project samples\Inquiry.Sample.Console\Inquiry.Sample.Console.csproj
```
