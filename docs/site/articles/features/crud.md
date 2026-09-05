# CRUD

The core Inquiry feature set: select, insert, update, upsert, delete. Every store gets these by adding `[InquirySelectAll]`, `[InquiryInsert]`, etc. attributes to partial method declarations.

This page shows the **complete, unedited generator output** for a `Shipper` store. It is the canonical example — every other feature page is a delta on top of this baseline.

## Supported operations

| Attribute | Return shape | What it emits |
|---|---|---|
| `[InquirySelectAll]` | `Task<IReadOnlyList<T>>` or `IAsyncEnumerable<T>` | `SELECT <columns> FROM <table>` |
| `[InquirySelectOneByKey]` | `Task<T?>` | `SELECT <columns> FROM <table> WHERE <pk> = @key` |
| `[InquirySelectAllByField("Col1", "Col2")]` | `Task<IReadOnlyList<T>>` | `SELECT … WHERE Col1 = @Col1 AND Col2 = @Col2` |
| `[InquirySelectAllByField]` (field-less) | `Task<IReadOnlyList<T>>` | Filter columns **derived from the method name** — see below |
| `[InquiryInsert]` | `Task<int>` or `Task<T?>` | Inserts one entity; the return type selects rows-affected or returning SQL |
| `[InquiryInsert]` with `IEnumerable<T>` | `Task<int>` | Batched inserts in one transaction |
| `[InquiryUpdate]` | `Task<bool>` or `Task<T?>` | Updates an entity by primary key; the return type selects the command shape |
| `[InquiryUpdate]` with `IEnumerable<T>` | `Task<int>` | Batched updates, each row matched by primary key |
| `[InquiryUpdate]` + `[InquiryWhere]` | `Task<int>` | Partially updates inferred columns for matching rows |
| `[InquiryUpsert]` | `Task<int>` or `Task<T?>` | Dialect-specific, with returning SQL selected by the return type |
| `[InquiryDelete]` | `Task<bool>` or `Task<T?>` | Deletes one row by primary key; the return type selects the command shape |
| `[InquiryDelete]` + `[InquiryWhere]` | `Task<int>` | Deletes rows matching the predicate |
| `[InquiryDeleteAll]` | `Task<int>` | Explicitly deletes every row |
| `[InquiryHardDelete]` | `Task<bool>` or `Task<T?>`; `Task<int>` with `[InquiryWhere]` | Bypasses soft delete for one row or for rows matching a predicate |
| `[InquiryHardDeleteAll]` | `Task<int>` | Bypasses soft delete for every row |

For single-entity insert, update, upsert, and key-delete methods, `Task<TEntity?>` selects the
returning SQL shape. Non-returning methods use `Task<int>` or `Task<bool>` as shown above. Batch and
predicate mutations cannot return entities, so their return type remains `Task<int>`.

## You write

### The entity

```csharp
using Inquiry.Entities;

namespace Inquiry.Northwind.Models;

[InquiryTable("Shippers")]
public sealed class Shipper
{
    [InquiryKey("ShipperID", IsGenerated = true)]
    public int? ShipperID { get; set; }

    [InquiryColumn]
    public string CompanyName { get; set; } = string.Empty;

    [InquiryColumn]
    public string? Phone { get; set; }
}
```

### The store

```csharp
using Inquiry.Northwind.Models;
using Inquiry.Stores;

namespace Inquiry.Northwind.Stores;

public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Shipper?> SelectByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("CompanyName")]
    public partial Task<IReadOnlyList<Shipper>> SelectByCompanyNameAsync(string companyName, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public partial Task<Shipper?> InsertReturningAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryUpsert]
    public partial Task<int> UpsertAsync(Shipper shipper, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteByKeyAsync(int? shipperID, CancellationToken cancellationToken = default);
}
```

That's the entire source you write. There is no body — every method is `partial` with the body left to the generator.

For partial updates and predicate deletes, compose the operation with `[InquiryWhere]`. Update
parameters come first and map to entity properties or columns by name; predicate parameters follow:

```csharp
[InquiryUpdate]
[InquiryWhere(nameof(Shipper.ShipperID))]
public partial Task<int> UpdatePhoneAsync(string? phone, int? shipperID, CancellationToken ct = default);

[InquiryDelete]
[InquiryWhere(nameof(Shipper.CompanyName))]
public partial Task<int> DeleteByCompanyNameAsync(string companyName, CancellationToken ct = default);

[InquiryDelete]
[InquiryWhere(nameof(Shipper.ShipperID), Compare.In)]
public partial Task<int> DeleteByKeysAsync(IReadOnlyList<int?> shipperIDs, CancellationToken ct = default);

[InquiryDeleteAll]
public partial Task<int> DeleteAllAsync(CancellationToken ct = default);
```

`[InquiryDelete]` with neither key parameters nor `[InquiryWhere]` is rejected at compile time. The
separate `[InquiryDeleteAll]` attribute makes a table-wide operation explicit at the declaration site.
See [Set-based mutations](set-based-mutations.md) for predicate operators, soft-delete behavior, and
parameter rules.

## The generator emits

### Materializer — `Shipper.InquiryEntity.g.cs`

Two materializers (a class implementation and a struct one). Generated stores call the struct overloads; the JIT specializes per concrete struct so each `Materialize(reader)` call inlines.

```csharp
// <auto-generated />
#nullable enable
namespace Inquiry.Northwind.Models
{
internal sealed class ShipperInquiryEntityMaterializer
    : global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Inquiry.Northwind.Models.Shipper>
{
    public global::Inquiry.Northwind.Models.Shipper Materialize(global::System.Data.Common.DbDataReader reader)
    {
        return new global::Inquiry.Northwind.Models.Shipper
        {
            ShipperID   = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0),
            CompanyName = reader.GetString(1),
            Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
        };
    }
}

internal readonly struct ShipperInquiryEntityStructMaterializer
    : global::Inquiry.Materialization.IInquiryEntityMaterializer<global::Inquiry.Northwind.Models.Shipper>
{
    public global::Inquiry.Northwind.Models.Shipper Materialize(global::System.Data.Common.DbDataReader reader)
    {
        return new global::Inquiry.Northwind.Models.Shipper
        {
            ShipperID   = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0),
            CompanyName = reader.GetString(1),
            Phone       = reader.IsDBNull(2) ? null : reader.GetString(2),
        };
    }
}
}
```

Each column is read by ordinal in ascending order. Nullable columns get the `IsDBNull(i) ? null : GetX(i)` pattern. This is the exact shape required for `CommandBehavior.SequentialAccess`, which the runtime pipeline uses.

### Store — `ShipperStore.InquiryStore.g.cs`

The baked SQL consts and the partial method bodies. (SQLite dialect shown; SQL flavors differ across providers — see [Providers](../providers/sqlite.md).)

```csharp
// <auto-generated />
#nullable enable
namespace Inquiry.Northwind.Stores
{
partial class ShipperStore
{
    private const string _sqlSelectAll        = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\"";
    private const string _sqlSelectByKey      = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\" WHERE \"ShipperID\" = @ShipperID";
    private const string _sqlInsert           = "INSERT INTO \"Shippers\" (\"CompanyName\", \"Phone\") VALUES (@CompanyName, @Phone)";
    private const string _sqlUpdate           = "UPDATE \"Shippers\" SET \"CompanyName\" = @CompanyName, \"Phone\" = @Phone WHERE \"ShipperID\" = @ShipperID";
    private const string _sqlUpsert           = "INSERT INTO \"Shippers\" (\"ShipperID\", \"CompanyName\", \"Phone\") VALUES (@ShipperID, @CompanyName, @Phone) ON CONFLICT (\"ShipperID\") DO UPDATE SET \"CompanyName\" = @CompanyName, \"Phone\" = @Phone";
    private const string _sqlInsertReturning  = "INSERT INTO \"Shippers\" (\"CompanyName\", \"Phone\") VALUES (@CompanyName, @Phone) RETURNING \"ShipperID\", \"CompanyName\", \"Phone\"";
    private const string _sqlDeleteByKey      = "DELETE FROM \"Shippers\" WHERE \"ShipperID\" = @ShipperID";
    private const string _sqlSelectBy_CompanyName = "SELECT \"ShipperID\", \"CompanyName\", \"Phone\" FROM \"Shippers\" WHERE \"CompanyName\" = @CompanyName";

    public ShipperStore(global::Inquiry.IInquiry inquiry) : base(inquiry) { }

    public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(CancellationToken cancellationToken)
        => Inquiry.QueryListAsync<Shipper, ShipperInquiryEntityStructMaterializer>(
            new global::Inquiry.Commands.InquiryCommand(_sqlSelectAll),
            default,
            cancellationToken);

    public async partial Task<Shipper?> SelectByKeyAsync(int? shipperID, CancellationToken cancellationToken)
        => await Inquiry.QuerySingleOrDefaultAsync<Shipper, int?, ShipperInquiryEntityStructMaterializer>(
            _sqlSelectByKey,
            shipperID,
            static (_cmd, _key) =>
            {
                var _p0 = _cmd.CreateParameter();
                _p0.ParameterName = "@ShipperID";
                _p0.DbType = global::System.Data.DbType.Int32;
                _p0.Value = (object?)_key ?? global::System.DBNull.Value;
                _cmd.Parameters.Add(_p0);
            },
            default,
            cancellationToken).ConfigureAwait(false);

    public partial Task<IReadOnlyList<Shipper>> SelectByCompanyNameAsync(string companyName, CancellationToken cancellationToken)
        => Inquiry.QueryListAsync<Shipper, string, ShipperInquiryEntityStructMaterializer>(
            _sqlSelectBy_CompanyName,
            companyName,
            static (_cmd, _arg) =>
            {
                var _p0 = _cmd.CreateParameter();
                _p0.ParameterName = "@CompanyName";
                _p0.DbType = global::System.Data.DbType.String;
                _p0.Value = (object?)_arg ?? global::System.DBNull.Value;
                _cmd.Parameters.Add(_p0);
            },
            default,
            cancellationToken);

    public partial Task<int> InsertAsync(Shipper shipper, CancellationToken cancellationToken)
        => Inquiry.ExecuteAsync(
            _sqlInsert,
            shipper,
            static (_cmd, _e) =>
            {
                var _p0 = _cmd.CreateParameter();
                _p0.ParameterName = "@CompanyName";
                _p0.DbType = global::System.Data.DbType.String;
                _p0.Value = (object?)_e.CompanyName ?? global::System.DBNull.Value;
                _cmd.Parameters.Add(_p0);
                var _p1 = _cmd.CreateParameter();
                _p1.ParameterName = "@Phone";
                _p1.DbType = global::System.Data.DbType.String;
                _p1.Value = (object?)_e.Phone ?? global::System.DBNull.Value;
                _cmd.Parameters.Add(_p1);
            },
            cancellationToken);

    // ...UpdateAsync, UpsertAsync, InsertReturningAsync, DeleteByKeyAsync follow the same pattern.
}
}
```

## What's notable in the output

- **The SQL is baked.** `_sqlSelectAll` is a `const string` — the compiler embeds it directly into the IL. The runtime reads it once and hands it to ADO.NET.
- **No string formatting at run time.** Parameter binding is `_p0.Value = _e.CompanyName` — a plain field write, no interpolation.
- **Binders are `static` lambdas.** The compiler emits a single cached delegate per method; no per-call closure allocation.
- **`DbType` is pre-computed.** The generator looks up the right `DbType` from your column's CLR type, so the binder doesn't pay reflection costs.
- **Reads pass `CommandBehavior.SingleResult`** at the pipeline level; generated-store reads (struct materializers) additionally pass `SequentialAccess` so the row streams forward-only. Both are wired in `InquiryRequestPipeline`, not visible at the call site. (`CommandBehavior.SingleRow` is deliberately omitted from single-row reads — it would let providers stop after the first row, suppressing the `QuerySingleOrDefaultAsync` multi-row throw.)

## Key generation: sequential GUIDs

Random `Guid.NewGuid()` keys fragment clustered B-tree indexes — every insert lands at a random page. For client-supplied GUID keys, opt into time-ordered sequential generation:

```csharp
[InquiryTable("Documents")]
public sealed class Document
{
    [InquiryKey(SequentialGuid = true)]
    public Guid Id { get; set; }          // Guid? works too

    [InquiryColumn] public string Title { get; set; } = "";
}
```

Insert, upsert, and batch-insert methods then assign a sequential GUID whenever the key is unset (`Guid.Empty` or `null`). The layout is **dialect-aware**: UUIDv7 on PostgreSQL, MySQL, MariaDB, SQLite, and Oracle; a SQL Server-optimized layout (timestamp in bytes [10..15], version 8) for `uniqueidentifier`, which compares those bytes first:

```csharp
var doc = new Document { Title = "spec" };
await store.InsertAsync(doc);
// doc.Id is now a sequential GUID — time-ordered for the target provider,
// observable by the caller, usable for follow-up reads.
```

- **Supplied keys win.** A non-empty key is never overwritten.
- **The entity is mutated** so you see the generated key after the call — same ergonomics as a database-generated identity.
- **`InquiryGuid.NewVersion7()`** is public; use it directly anywhere you need a UUIDv7. On .NET 9+ it delegates to `Guid.CreateVersion7()`; on .NET 8 it's an RFC 9562-conformant polyfill. For SQL Server-ordered keys, use `InquiryGuid.NewSqlServerSequential()`.
- `SequentialGuid` requires a plain client-supplied `Guid`/`Guid?` key — combining it with `IsGenerated` or `UseDatabaseDefault`, or putting it on a non-Guid key, is a build-time error (`INQ047`).

## Derived query methods

Leave `[InquirySelectAllByField]` **field-less** and the filter columns are inferred from the method name — the Spring Data convention, resolved at compile time:

```csharp
// No field argument — "CompanyName" comes from the method name.
[InquirySelectAllByField]
public partial Task<IReadOnlyList<Shipper>> SelectByCompanyNameAsync(string companyName, CancellationToken ct = default);

// Multiple fields: the name splits on "And".
[InquirySelectAllByField]
public partial Task<IReadOnlyList<Customer>> SelectByCountryAndCityAsync(string country, string city, CancellationToken ct = default);
```

- The segment after the first PascalCase **`By`** names the fields; **`And`** word boundaries separate multiple (`SelectByCountryAndCityAsync` → `Country`, `City`). A trailing `Async` is ignored, and the leading verb (`Select`, `Find`, `Get`, …) is cosmetic.
- Each derived field resolves against the entity's mapped properties/columns exactly like an explicit one — an unknown field is the same compile error (`INQ007`). Parameters bind in field order.
- An explicit field list always wins; a field-less name with no `By<Field>` segment is a compile error (`INQ054`).

## Cross-dialect SQL differences

The same C# source produces dialect-specific SQL. A small sample:

| Operation | Sqlite / PostgreSQL | SQL Server | MySQL | Oracle |
|---|---|---|---|---|
| Quote identifier | `"Shippers"` | `[Shippers]` | `` `Shippers` `` | `Shippers` (unquoted; Oracle folds the bare name to upper-case) |
| Upsert | `ON CONFLICT (…) DO UPDATE` | `UPDATE … IF @@ROWCOUNT = 0 INSERT` | `ON DUPLICATE KEY UPDATE` | `MERGE` |
| Insert returning | `RETURNING …` | `OUTPUT INSERTED.*` | `LAST_INSERT_ID()` round trip | `RETURNING … INTO :out_*` |
| Parameter prefix | `@name` | `@name` | `@name` | `:name` (rewritten in factory) |

See [Providers](../providers/sqlite.md) for the per-dialect details.

## Upsert concurrency semantics

Upsert atomicity differs per dialect; the table below pins what each provider does so callers can pick a pattern that matches their concurrency expectations.

| Dialect | Client-supplied key | Database-generated key |
|---|---|---|
| SQLite | `INSERT ... ON CONFLICT (...) DO UPDATE` — single statement, atomic | `INSERT ... ON CONFLICT (key) DO UPDATE` (the key is included in the INSERT) — single statement, atomic |
| PostgreSQL | `INSERT ... ON CONFLICT (...) DO UPDATE` — single statement, atomic | `INSERT ... ON CONFLICT (key) DO UPDATE` on the explicit-key branch — atomic (the explicit key is supplied, so no sequence value is consumed) |
| MySQL | `INSERT ... ON DUPLICATE KEY UPDATE` — single statement, atomic | Integer `AUTO_INCREMENT` key: same `ON DUPLICATE KEY UPDATE` with `LAST_INSERT_ID(key)` echo. Non-auto `UseDatabaseDefault` key: null routes through insert; an explicit key uses ordinary upsert and selects by that key. A declared secondary unique constraint makes return-entity upsert ambiguous and produces `INQ039`; non-returning upsert remains supported. |
| MariaDB | `INSERT ... ON DUPLICATE KEY UPDATE` — single statement, atomic | Null routes through native insert-returning; explicit keys use the ordinary upsert. Native `RETURNING` reads the actual inserted or updated row directly (no user variable or trailing SELECT needed). |
| SQL Server | `UPDATE … IF @@ROWCOUNT = 0 INSERT` inside `BEGIN/COMMIT TRANSACTION` with `UPDLOCK, SERIALIZABLE` table hints — serializes concurrent same-key upserts, atomic with no duplicate-key race | Same update-first pattern on the explicit-key branch — atomic (the null/generate branch is a plain INSERT) |
| Oracle | `MERGE` — same race-condition class as SQL Server's `MERGE` | Not supported (`INQ039` build error): the join key is `NULL` on a generated-key upsert so `MERGE` can never match — use explicit Insert/Update instead. Configuring `INQ039` as a warning or `none` project-wide opts every unsupported project method into a throwing runtime stub. |

What the contract guarantees, on every dialect: **N concurrent upserts of the same key always end with exactly one row whose state matches one of the inputs**. The integration test `UpsertConcurrencyTests.ConcurrentUpsertsOfSameKeyEndInOneRowMatchingOneInput` pins this against each live provider.

What it does **not** guarantee on every dialect: that every parallel upsert succeeds. On Oracle, a duplicate-key failure on one parallel call is a known engine-level race and surfaces as an exception (SQL Server uses an update-first pattern with `UPDLOCK, SERIALIZABLE` hints, so all parallel upserts succeed). If your app must serialize on Oracle, wrap the upsert in an explicit transaction with an appropriate isolation level (`SERIALIZABLE`, or `READ COMMITTED` plus an advisory lock).

On **MySQL**, a non-`AUTO_INCREMENT` database-default key used by insert-returning must declare a
standalone scalar `DefaultExpression` matching the deployed schema (for example, `"(UUID())"`). Inquiry
evaluates that expression once into `@'__inquiry.generated-key'`, inserts the captured value, and selects
the row by the same value. `Guid` keys retain `UUID()` as a compatibility fallback. Insert-returning
intentionally ignores an entity's supplied value for a `UseDatabaseDefault` key. A nullable upsert with
a null key takes that insert path; an explicit key uses the ordinary upsert and selects by `@key`.
Because MySQL cannot safely identify a different primary key that wins a declared secondary-unique
conflict, that return-entity shape produces `INQ039`; use non-returning upsert or model the conflict
explicitly. Inquiry enables `AllowUserVariables=true` on MySQL connections for the capture batch.
**MariaDB** uses native `INSERT…RETURNING` instead, so it does not need the user variable or
`AllowUserVariables`.

On **MySQL** and **MariaDB**, an empty-SET upsert (an entity with only key columns and nothing to update)
must still emit an assignment because MySQL has no `DO NOTHING` equivalent. Client/non-auto keys use
the no-op `ON DUPLICATE KEY UPDATE key = key`; an `AUTO_INCREMENT` key uses
`key = LAST_INSERT_ID(key)` once so returning paths can recover the winning key.
The `Task<TEntity?>` variant therefore returns the matched row on conflict rather
than `null`, unlike PostgreSQL, SQLite, and SQL Server which return `null` when no columns are
modified. Design for this if your code branches on the returning upsert's null/non-null result.

## See also

- [Pagination](pagination.md) — `OrderBy`, `Paged = true`, and keyset paging.
- [Soft delete](soft-delete.md) — automatic `WHERE IsDeleted = 0` composition.
- [Batch operations](batch-operations.md) — multi-row inserts, updates, and deletes.
