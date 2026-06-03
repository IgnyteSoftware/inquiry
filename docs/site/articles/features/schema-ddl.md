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

## Indexes

`[InquiryIndex]` on an entity or `[InquiryColumn(Indexed = true)]` on a column emits `CREATE INDEX` statements alongside the table DDL.

## What it isn't

This is not a full migration framework. It's a **CREATE-from-scratch** script. For evolving an existing schema (add column, drop table, etc.), use a dedicated migration tool (DbUp, FluentMigrator, EF Migrations, Flyway) and treat `InquiryGeneratedSchema.Ddl` as the canonical reference for what the live schema should look like.
