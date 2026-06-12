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
services.AddInquiry();                                // core runtime services
services.AddInquiryGeneratedStores();                 // generated stores/materializers in this assembly
services.AddInquirySqlite("Data Source=:memory:");    // or AddInquirySqlServer / AddInquiryPostgreSql / etc.

var provider = services.BuildServiceProvider();
```

`AddInquiryGeneratedStores()` calls the generator's `InquiryGeneratedServiceRegistration` - every store you declared is registered as scoped, matching `IInquiry`'s DI lifetime.

Every provider also has an `IConfiguration` overload that resolves the connection string by name
(`ConnectionStrings:Inquiry` by default), the standard ASP.NET Core shape:

```csharp
builder.Services.AddInquirySqlite(builder.Configuration);                  // ConnectionStrings:Inquiry
builder.Services.AddInquirySqlServer(builder.Configuration, "Northwind"); // ConnectionStrings:Northwind
```

A missing connection string throws at registration time with the exact configuration key named.

## 6. Run a query

```csharp
var store = provider.GetRequiredService<ShipperStore>();

await store.InsertAsync(new Shipper { CompanyName = "Speedy Express", Phone = "(503) 555-9831" });
var all = await store.SelectAllAsync();
```

For a one-off reporting query whose shape isn't an entity, you don't need a store at all — mark a plain DTO with `[InquiryAdHoc]` and pass hand-written SQL to the `IInquiry` facade (interpolated values become bound parameters):

```csharp
[InquiryAdHoc]
public sealed class ShipperOrderCount
{
    public string CompanyName { get; set; } = "";   // ordinal 0
    public int Orders { get; set; }                 // ordinal 1
}

var inquiry = provider.GetRequiredService<IInquiry>();
var counts = await inquiry.QueryListAsync<ShipperOrderCount>(
    $"SELECT s.CompanyName, COUNT(o.OrderID) FROM Shippers s LEFT JOIN Orders o ON o.ShipVia = s.ShipperID GROUP BY s.CompanyName");
```

Properties map to SELECT-list positions in declaration order — see [Ad-hoc DTOs](features/ad-hoc-dtos.md).

## 7. Wrap multiple calls in a transaction

`IInquiry.ExecuteInTransactionAsync()` opens an `IInquiryTransaction` that owns a connection and a `DbTransaction`, runs your delegate, and commits only when the delegate completes successfully. Every operation in the delegate — generated store methods *and* any ad-hoc SQL called directly on the handle — shares that transaction.

```csharp
var inquiry = provider.GetRequiredService<IInquiry>();
var shippers = provider.GetRequiredService<ShipperStore>();

await inquiry.ExecuteInTransactionAsync(async tx =>
{
    await shippers.InsertAsync(new Shipper { CompanyName = "Speedy Express" });    // joins the tx
    await tx.ExecuteAsync(                                                          // ad-hoc, joins the tx
        $"UPDATE Shippers SET Phone = {"555-1212"} WHERE CompanyName = {"Speedy Express"}");
});
```

Key points:

- **The helper owns commit/rollback.** `ExecuteInTransactionAsync` commits on success. If the delegate throws, dispose rolls back.
- **Stores join automatically.** No `WithTransaction` builder, no per-call parameter. The transaction is *ambient* — once open, every Inquiry call on the same async flow uses it.
- **Two call styles, same outcome.** `tx.ExecuteAsync(...)` for ad-hoc SQL; `store.X(...)` for typed generated methods. Both run on the same connection in the same transaction.
- **Use-after-close fails fast.** Calling `tx.X(...)` after `Commit` / `Rollback` / `Dispose` throws `ObjectDisposedException`. Store calls from async work that captured the transaction also throw after close; fresh store calls after the transaction scope use the default non-transactional pipeline.
- **Nested calls become savepoints.** `await using var sp = await tx.BeginTransactionAsync()` emits `SAVEPOINT`. Inner commit releases it; inner rollback reverts just that scope; the outer transaction continues.

```csharp
await using var outer = await inquiry.BeginTransactionAsync();
var startEvent = "start";
await outer.ExecuteAsync($"INSERT INTO Audit (Event) VALUES ({startEvent})");

await using (var inner = await outer.BeginTransactionAsync())   // SAVEPOINT
{
    try { await DoRiskyAsync(inner); await inner.CommitAsync(); }
    catch { await inner.RollbackAsync(); }   // outer still has the audit row
}

await outer.CommitAsync();
```

The [transactions feature page](features/transactions.md) covers isolation levels, nested savepoints, the in-flight concurrency guard, and what's not supported (e.g. `TransactionScope`).

## 8. (Optional) Get the schema DDL

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
- A **DI registration class** - `InquiryGeneratedServiceRegistration` - that wires up every store and materializer.

For the **full annotated generator output** of the example above, see the [CRUD feature page](features/crud.md).

## Troubleshooting: red squiggles under `partial` methods

Your IDE runs the source generator live, so a valid `partial` store method gets its generated body
(and IntelliSense) immediately — **no build required**. If a method stays red with *"partial method
must have an implementation"* (CS8795), the generator didn't run for that declaration. Check, in
order:

1. **Is a provider package referenced?** The generator ships inside the provider package
   (`Inquiry.Sqlite`, `Inquiry.SqlServer`, …) — the core `Inquiry` package alone generates nothing.
2. **Is the dialect resolved?** Generation only fires when the assembly's dialect is known — either
   a referenced provider's `[assembly: InquiryDialect]` marker or your own. Look for `INQ0xx`
   diagnostics in the Error List; they explain what was skipped and why.
3. **Did you just update the Inquiry package?** Visual Studio can keep the previous generator
   loaded — restart the IDE.
4. **Building Inquiry itself from source?** In this repository the analyzer is attached via built
   DLL paths, so run `dotnet build` once after a fresh clone or `git clean` before the IDE can load
   it. NuGet consumers are unaffected.

A persistent CS8795 alongside an `INQ039` warning is different — it means the active dialect cannot
emit that operation, and the method body is a throwing stub by design.

## Next steps

- **[How it works](concepts.md)** — the compile-time pipeline explained end-to-end.
- **[Features](features/crud.md)** — pagination, soft delete, batch operations, FTS, projections, and more.
- **[Providers](providers/sqlite.md)** — per-dialect notes.
