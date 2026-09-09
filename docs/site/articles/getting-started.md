# Getting started

This example creates a SQLite database, inserts a shipper, and reads it back using generated store
methods. It targets .NET 8; Inquiry also supports .NET 9 and .NET 10.

## Create the project

```bash
dotnet new console -n InquiryGettingStarted --framework net8.0
cd InquiryGettingStarted
dotnet add package Ignyte.Inquiry.Sqlite --prerelease
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.2
```

Inquiry is currently available as a preview. The provider package includes the source generator and
references the core `Ignyte.Inquiry` runtime. Package IDs start with `Ignyte.`; C# namespaces start
with `Inquiry`.

The SQLite provider supplies the dialect marker automatically. This single-provider example needs
no `AssemblyInfo.cs`. An explicit `[assembly: Inquiry.InquiryDialect("Sqlite")]` is only needed when
you deliberately override dialect inference, for example when referenced providers make the choice
ambiguous. The selected dialect must match the connection used at runtime.

## Add the entity and store

Create **Shipper.cs**:

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

Create **ShipperStore.cs**:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquiryInsert]
    public partial Task<Shipper?> InsertReturningAsync(
        Shipper shipper, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(
        CancellationToken ct = default);
}
```

The database generates `ShipperID`. The insert-returning method reads the saved row into a result
entity; use that result to obtain the key. An insert returning `Task<int>` instead reports affected
rows, not the key, and does not copy the database-generated identity into the input object.

`SelectAllAsync` returns a buffered list. Use `IAsyncEnumerable<Shipper>` on a separate
`[InquirySelectAll]` method when you explicitly want streaming; enumerate and dispose it inside the
service scope.

## Replace Program.cs

```csharp
using System;
using System.Threading;
using Inquiry.DependencyInjection;
using Inquiry.Generated;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

const string connectionString = "Data Source=inquiry-getting-started.db";
var ct = CancellationToken.None;

// Schema creation is explicit and must finish before the first store operation.
await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = InquiryGeneratedSchema.Ddl;
    await command.ExecuteNonQueryAsync(ct);
}

var services = new ServiceCollection();
services.AddInquiry();
services.AddInquiryGeneratedStores();
services.AddInquirySqlite(connectionString);

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var store = scope.ServiceProvider.GetRequiredService<ShipperStore>();

var saved = await store.InsertReturningAsync(
    new Shipper { CompanyName = "Speedy Express" }, ct);
if (saved is null)
{
    throw new InvalidOperationException("The insert did not return a shipper.");
}

Console.WriteLine($"Saved shipper {saved.ShipperID}: {saved.CompanyName}");
var all = await store.SelectAllAsync(ct);
foreach (var shipper in all)
{
    Console.WriteLine($"{shipper.ShipperID}: {shipper.CompanyName}");
}
```

This is a complete program. All Inquiry calls use the same file database even though each operation
opens its own connection. A private `Data Source=:memory:` database would disappear when its creating
connection closes; it is not interchangeable with this example.

The database file is created in the working directory. Run this first-use example with a new file:
the generated DDL is initial schema creation, not a migration or an idempotent startup routine. To
run it again from scratch, delete only this example's `inquiry-getting-started.db` file after the
process exits, or choose a new filename. For an existing database, run migrations separately and omit
the initial DDL block. DI registration never creates tables.

## Build and run

```bash
dotnet build
dotnet run --no-build
```

On a new database, the program prints the saved key and the one shipper in the list:

```text
Saved shipper 1: Speedy Express
1: Speedy Express
```

Stores and `IInquiry` are scoped services. Console applications must create a scope as above;
ASP.NET Core provides a request scope. For stores in multiple assemblies, expose a uniquely named
public registration wrapper in each data library, and configure core services and the provider once
in the host.

## Troubleshooting: red squiggles under partial methods

If a partial method reports CS8795, inspect the generator diagnostics first.

1. Reference a provider such as `Ignyte.Inquiry.Sqlite`; `Ignyte.Inquiry` alone contains no generator.
2. Check that dialect inference selects the provider you intend. An `INQ` diagnostic explains
   unresolved or invalid declarations.
3. After a package update, restart the IDE if it still uses the old generator.
4. When building Inquiry itself from source, build once so its locally attached analyzer DLLs exist.
   NuGet consumers receive those DLLs in the provider package.

An `INQ039` error means the chosen provider does not support the declared operation. Fix the
declaration or choose a supported provider. Project-wide warning or suppression configuration for
`INQ039` deliberately changes unsupported operations into generated throwing stubs.

## Next steps

- [CRUD](features/crud.md) covers reads, inserts, updates, and returning forms.
- [Transactions](features/transactions.md) shows atomic operations across stores and ad-hoc SQL.
- [SQLite](providers/sqlite.md) describes provider behavior.
- [How it works](concepts.md) and [Architecture](architecture.md) explain generated SQL and materialization.
