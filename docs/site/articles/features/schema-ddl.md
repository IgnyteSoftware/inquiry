# Schema DDL

The generator emits a `CREATE TABLE` script for every entity in your assembly, in dependency order, as a `const string` on `InquiryGeneratedSchema`. Use it for test bootstrapping, first-run setup, or as the starting point for a migration.

## Usage

```csharp
using Inquiry.Generated;
using Microsoft.Data.Sqlite;

await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
await using var cmd = connection.CreateCommand();
cmd.CommandText = InquiryGeneratedSchema.Ddl;
await cmd.ExecuteNonQueryAsync();
```

## What gets emitted

For an assembly with `Category`, `Product`, `Supplier` entities (where `Product` has FK columns to both):

```sql
CREATE TABLE IF NOT EXISTS "Categories" (
    "CategoryID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "CategoryName" TEXT NOT NULL,
    "Description" TEXT
);

CREATE TABLE IF NOT EXISTS "Suppliers" (
    "SupplierID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "CompanyName" TEXT NOT NULL,
    ...
);

CREATE TABLE IF NOT EXISTS "Products" (
    "ProductID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ProductName" TEXT NOT NULL,
    "SupplierID" INTEGER REFERENCES "Suppliers"("SupplierID"),
    "CategoryID" INTEGER REFERENCES "Categories"("CategoryID"),
    ...
);
```

Tables are emitted in dependency order — referenced tables before their dependents — so the script runs without FK violations.

## Per-dialect DDL flavor

Each provider analyzer emits the right flavor:

- **Sqlite**: `"Quoted"` identifiers, `INTEGER PRIMARY KEY AUTOINCREMENT`, `TEXT`/`INTEGER`/`REAL` column types.
- **SQL Server**: `[Bracketed]` identifiers, `IDENTITY(1,1)`, `NVARCHAR(MAX)` / `BIT` / `DATETIME2`.
- **PostgreSQL**: `"Quoted"` identifiers, `GENERATED ALWAYS AS IDENTITY` (or `SERIAL` legacy), `TEXT` / `BOOLEAN` / `TIMESTAMPTZ`.
- **MySQL**: `` `Backticked` `` identifiers, `AUTO_INCREMENT`, `VARCHAR(N)` / `TINYINT(1)` / `DATETIME`.
- **Oracle**: `"UPPER_CASED"` identifiers, `GENERATED ALWAYS AS IDENTITY` (12c+), `VARCHAR2(N)` / `NUMBER(1)` / `TIMESTAMP`.

## Server-computed columns

`[InquiryColumn(Computed = "<sql expression>")]` makes a **server-computed column** (EF `HasComputedColumnSql` / XPO persistent-alias analog) — the database calculates the value from the expression:

```csharp
[InquiryColumn("FullName", Computed = "FirstName || ' ' || LastName")]
public string FullName { get; set; } = "";
```

- **Excluded from generated INSERT/UPDATE** (the database owns the value), but **selected and materialized** into the property on reads — and it recomputes automatically when its source columns change.
- The DDL emits each dialect's computed-column form: `AS (<expr>)` on SQLite / SQL Server / Oracle; the typed `<type> GENERATED ALWAYS AS (<expr>) STORED` on PostgreSQL and MySQL (which require it).
- A computed column can't also be a key, database-generated/defaulted, an auditing column, the soft-delete indicator, or a concurrency token (`INQ057`). The expression is **raw SQL** — keep untrusted input out of it.

## Indexes

`[InquiryColumn(IsIndexed = true)]` (or `IsUnique = true` for a UNIQUE index) on a column emits a `CREATE INDEX` statement alongside the table DDL.

## DDL safety lints (opt-in)

Inquiry ships advisory analyzer "lints" for risky schema shapes. They are **off by default** — turn them on in `.editorconfig` per ID when you want them:

```ini
# Surface a lint as a warning (or info/error) — opt in per ID
dotnet_diagnostic.INQ061.severity = warning
dotnet_diagnostic.INQ062.severity = warning
dotnet_diagnostic.INQ064.severity = warning
```

| ID | Lints | Why |
|---|---|---|
| **`INQ061`** | A foreign-key column with no index. | Most engines (SQL Server, PostgreSQL, Oracle, SQLite) don't auto-index foreign keys, so joins and `ON DELETE/UPDATE` cascades over the column scan the table. Add `IsIndexed = true` to the column's `[InquiryColumn]` / `[InquiryForeignKey]`. **MySQL/InnoDB auto-indexes FK constraints and is exempt.** |
| **`INQ062`** | A `decimal` column with no explicit precision/scale. | It silently takes the dialect default (e.g. `DECIMAL(18,2)`), which can round — a real hazard for money. Set `[InquiryColumn(Precision = …, Scale = …)]` (or `SqlType`). EF Core's `DecimalTypeDefaultWarning` is the same advisory. |
| **`INQ064`** | A column a store method filters on (a `[InquirySelectAllByField]` field or an `[InquiryWhere]` criterion) with no index. | Those queries scan the table. Add `[InquiryColumn(IsIndexed = true)]` (or `IsUnique`) to a column you filter often. |

Because they're off by default, the lints never break a build until you opt in — then they participate in `dotnet build` (and CI) at the severity you choose, just like any analyzer diagnostic.

## What it isn't

This is not a full migration framework. It's a **CREATE-from-scratch** script. For evolving an existing schema (add column, drop table, etc.), use a dedicated migration tool (DbUp, FluentMigrator, EF Migrations, Flyway) and treat `InquiryGeneratedSchema.Ddl` as the canonical reference for what the live schema should look like.
