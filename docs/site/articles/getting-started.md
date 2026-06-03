# Getting started

Inquiry is a Roslyn source generator. You write attributes; it writes the SQL and the C# that runs it. This walkthrough takes you from an empty .NET project to a running query in about five minutes.

## 1. Install

Pick a provider package — it transitively brings in the core `Inquiry` runtime and the matching source generator.

```bash
dotnet add package Inquiry.Sqlite        # — or —
dotnet add package Inquiry.SqlServer
dotnet add package Inquiry.PostgreSql
dotnet add package Inquiry.MySql
dotnet add package Inquiry.Oracle
```

## 2. Pick a dialect (one per assembly)

Add an `AssemblyInfo.cs` to your project:

```csharp
[assembly: Inquiry.InquiryDialect("Sqlite")]
```

This tells the generator which dialect to emit. The attribute is `AllowMultiple = false` — exactly one dialect per assembly. (If you need to target multiple databases, split your entities across assemblies.)

## 3. Declare an entity

```csharp
using Inquiry.Entities;

[InquiryTable("Shippers")]
public sealed class Shipper
{
    [InquiryKey("ShipperID", IsGenerated = true)]
    public int? ShipperID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = "";

    [InquiryColumn]
    public string? Phone { get; set; }
}
```

- `[InquiryTable("...")]` — the database table name.
- `[InquiryKey]` — the primary key. `IsGenerated = true` means the database fills it in (`IDENTITY`, `SERIAL`, `AUTOINCREMENT`, etc.).
- `[InquiryColumn]` — a mapped column. The column name defaults to the property name.

## 4. Declare a store

A store is a `partial class` deriving from `InquiryStore<T>`. Each method is `partial`, decorated with an operation attribute, with no body — you don't write the body, the generator does.

```csharp
using Inquiry.Stores;

public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Shipper?> SelectByKeyAsync(int? id, CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Shipper shipper, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Shipper shipper, CancellationToken ct = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteByKeyAsync(int? id, CancellationToken ct = default);
}
```

## 5. Wire up dependency injection

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddInquirySqlite("Data Source=:memory:");   // — or AddInquirySqlServer / AddInquiryPostgreSql / etc.
services.AddInquiryGeneratedStores();                 // registers every generated store in this assembly

var provider = services.BuildServiceProvider();
```

`AddInquiryGeneratedStores()` calls the generator's `InquiryGeneratedServiceRegistration` — every store you declared is registered as a singleton.

## 6. Run a query

```csharp
var store = provider.GetRequiredService<ShipperStore>();

await store.InsertAsync(new Shipper { CompanyName = "Speedy Express", Phone = "(503) 555-9831" });
var all = await store.SelectAllAsync();
```

## 7. (Optional) Get the schema DDL

The generator also emits `InquiryGeneratedSchema.Ddl` — the CREATE TABLE statements for every Inquiry entity in your assembly, ordered so referenced tables precede their dependents. Useful for test bootstrapping and first-run setup.

```csharp
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var cmd = connection.CreateCommand();
cmd.CommandText = Inquiry.Generated.InquiryGeneratedSchema.Ddl;
await cmd.ExecuteNonQueryAsync();
```

## What just happened

Behind the scenes, the source generator turned your `partial` declarations into:

- A **materializer** (`ShipperInquiryEntityStructMaterializer`) — reads each column from a `DbDataReader` by ordinal into a new `Shipper`.
- A **partial store class** — for each method, a private `const string _sql...` field with the baked SQL, plus a body that calls the request pipeline.
- A **DI registration class** — `InquiryGeneratedServiceRegistration` — that wires up every store as a singleton.

For the **full annotated generator output** of the example above, see the [CRUD feature page](features/crud.md).

## Next steps

- **[How it works](concepts.md)** — the compile-time pipeline explained end-to-end.
- **[Features](features/crud.md)** — pagination, soft delete, batch operations, FTS, projections, and more.
- **[Providers](providers/sqlite.md)** — per-dialect notes.
