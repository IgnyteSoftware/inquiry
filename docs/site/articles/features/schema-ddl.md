# Schema DDL

The generator emits a `CREATE TABLE` script for every entity in your assembly, in dependency order, as a `const string` on `InquiryGeneratedSchema`. Use it for test bootstrapping, first-run setup, or as the starting point for a migration.

Set `[InquiryTable("Name", GenerateDdl = false)]` for a mapping whose table is managed by hand-written migrations or by another canonical mapping. The entity remains available to generated stores and materializers, but it is completely excluded from schema DDL, schema validation, indexes, checks, and foreign-key analysis.

On SQL Server, a non-nullable `byte[]` marked `[InquiryConcurrencyToken(DatabaseGenerated = true)]` is emitted as `ROWVERSION NOT NULL`. Ordinary `byte[]` columns remain `VARBINARY(MAX)`. Returning mutations capture a rowversion through an internal `BINARY(8)` output table; `ROWVERSION` is used only for the physical table column.

For providers that require database objects to bind collections, the same class also exposes:

- `ProviderArtifactsDdl` - additive, idempotent setup DDL for compile-time-discovered artifacts.
- `ProviderArtifactsValidationSql` - a read-only query that returns one row per missing artifact.
- `Ddl` - the compatible full baseline: provider artifacts first, followed by table/index DDL.

SQL Server uses these constants for schema-qualified TVP types. Applying artifacts is an explicit deployment step; generated commands never create or discover types at runtime.

## Expected-schema manifest

The same final normalized schema graph also produces `SchemaManifestJson`, `SchemaManifestSha256`,
`SchemaManifestFormatVersion`, and `SchemaManifestChunkCount` on `InquiryGeneratedSchema`. Manifest v1
records semantic provider-rendered tables, columns and final store types, keys, indexes, checks, foreign
keys, and provider artifacts. It intentionally excludes DDL guards, inline-versus-deferred constraint
placement, CLR property/type names, source paths, timestamps, and declaration order that does not change
the physical schema. The lowercase SHA-256 fingerprint covers the exact UTF-8 JSON bytes.

Tools can read the manifest without loading application code through assembly metadata keys
`Inquiry.SchemaManifest.FormatVersion`, `Inquiry.SchemaManifest.Sha256`,
`Inquiry.SchemaManifest.ChunkCount`, and `Inquiry.SchemaManifest.Chunk.0000` onward. Concatenate chunks
in numeric order; each is at most 12,288 UTF-8 bytes. Manifest v1 property order and token meanings are
stable. Additive fields may be ignored; removing, renaming, or changing an existing field requires a new
format version.

This release emits expected metadata only. It does not connect to a database, compare or apply schemas,
refresh offline metadata, or generate migrations. The live/offline validation CLI and comparison policy
belong to the separate validation workstream (#72).

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
    "SupplierID" INTEGER,
    "CategoryID" INTEGER,
    ...
    FOREIGN KEY ("SupplierID") REFERENCES "Suppliers"("SupplierID"),
    FOREIGN KEY ("CategoryID") REFERENCES "Categories"("CategoryID")
);
```

Tables are emitted in dependency order — referenced tables before their dependents — so the script runs without FK violations.

### Cyclic foreign keys

When two or more tables reference each other, no table ordering can make every referenced table exist
first. Inquiry detects those cycles and handles only the foreign keys inside the cycle specially:

- SQLite keeps cyclic foreign keys inline because SQLite accepts forward references in `CREATE TABLE`
  and does not support `ALTER TABLE ... ADD CONSTRAINT`.
- SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle create all tables first, then add the cyclic
  foreign keys with named `ALTER TABLE ... ADD CONSTRAINT` statements.
- Self-referencing foreign keys remain inline on every provider.
- Foreign keys entering or leaving a cycle remain inline and retain normal dependency ordering.

Deferred constraint names are deterministic, hash-suffixed, and at most 63 UTF-8 bytes, so regenerated
baselines use stable names within every supported provider's identifier limit. The emitted phases are
provider artifacts, tables, deferred cyclic constraints, then indexes.

`Ddl` is a run-once baseline script. In particular, deferred `ALTER TABLE` statements are not guarded
against an existing constraint. Execute it against an empty database/schema, or translate the statements
into the idempotency conventions of your migration tool.

## Per-dialect DDL flavor

Each provider analyzer emits the right flavor:

- **Sqlite**: `"Quoted"` identifiers, `INTEGER PRIMARY KEY AUTOINCREMENT`, `TEXT`/`INTEGER`/`REAL` column types.
- **SQL Server**: `[Bracketed]` identifiers, `IDENTITY(1,1)`, `NVARCHAR(MAX)` / `BIT` / `DATETIME2`.
- **SQL Server collection artifacts**: deterministic `CREATE TYPE [schema].[Inquiry_Tvp_<sha256>] AS TABLE ([Value] ... NOT NULL)` statements for supported positive `IN` and `[InquiryDeleteAll]` usages. Negative `NOT IN` expansion does not require a TVP artifact.
- **PostgreSQL**: `"Quoted"` identifiers, `SERIAL` / `BIGSERIAL` keys (64-bit keys use `BIGSERIAL`), `TEXT` / `BOOLEAN` / `TIMESTAMPTZ`.
- **MySQL**: `` `Backticked` `` identifiers, `AUTO_INCREMENT`, `VARCHAR(N)` / `TINYINT(1)` / `DATETIME`.
- **Oracle**: unquoted identifiers (Oracle folds them to upper-case; a name that isn't legal unquoted, e.g. with an embedded space, is double-quoted), `GENERATED BY DEFAULT AS IDENTITY` (12c+), `VARCHAR2(N)` / `NUMBER(1)` / `TIMESTAMP`.

> [!NOTE]
> Oracle has no `CREATE TABLE IF NOT EXISTS`. The generated DDL emits a plain `CREATE TABLE`, which raises `ORA-00955` if the table already exists. For idempotent migration scripts, wrap each statement in a PL/SQL block:
> ```sql
> BEGIN EXECUTE IMMEDIATE 'CREATE TABLE …'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;
> ```
> Other dialects guard table creation (`CREATE TABLE IF NOT EXISTS`, or `IF OBJECT_ID(…) IS NULL` on SQL Server),
> but the complete baseline is not replayable when it contains deferred cyclic constraints or unguarded indexes.

## Server-computed columns

`[InquiryColumn(Computed = "<sql expression>")]` makes a **server-computed column** (EF `HasComputedColumnSql` / XPO persistent-alias analog) — the database calculates the value from the expression:

```csharp
[InquiryColumn("FullName", Computed = "FirstName || ' ' || LastName")]
[InquiryComputedExpression("mysql", "CONCAT(FirstName, ' ', LastName)")]
[InquiryComputedExpression("mariadb", "CONCAT(FirstName, ' ', LastName)")]
public string FullName { get; set; } = "";
```

- **Excluded from generated INSERT/UPDATE** (the database owns the value), but **selected and materialized** into the property on reads — and it recomputes automatically when its source columns change.
- The DDL emits each dialect's computed-column form: `AS (<expr>)` on SQLite / SQL Server / Oracle; the typed `<type> GENERATED ALWAYS AS (<expr>) STORED` on PostgreSQL and MySQL (which require it).
- Inquiry reports provably unsafe lexical shapes as `INQ072`, including unterminated quoting/comments, unmatched parentheses, statement separators, subqueries, and window expressions. This is conservative validation, not a full provider SQL parser.
- `InquiryComputedExpression` supplies a provider-specific override while `Computed` remains the required fallback. Stable provider ids are `sqlite`, `sqlserver`, `postgresql`, `mysql`, `mariadb`, and `oracle`. MySQL and MariaDB reject a real `||` token because its meaning depends on SQL mode; use `CONCAT(...)`, `OR`, or an override.
- A computed column can't also be a key, database-generated/defaulted, an auditing column, the soft-delete indicator, or a concurrency token (`INQ057`). The expression is **raw SQL** — keep untrusted input out of it.

## Indexes

`[InquiryColumn(IsIndexed = true)]` (or `IsUnique = true` for a UNIQUE index) on a column emits a `CREATE INDEX` statement alongside the table DDL.

Use repeatable class-level indexes for ordered composite keys, uniqueness, and covering columns:

```csharp
[InquiryIndex(nameof(TenantId), nameof(Code), IsUnique = true)]
[InquiryIndex(nameof(CategoryId), Name = "IX_Product_Category", Include = new[] { nameof(DisplayName) })]
public sealed class Product { /* ... */ }
```

`Include` emits non-key covering columns on SQL Server and PostgreSQL. SQLite, MySQL, MariaDB, and
Oracle cannot represent this distinction faithfully, so their analyzers report an error instead of
silently changing the index key. Composite and unique indexes work on all six providers. Existing
single-column flags and their short `IX_`/`UX_` names remain unchanged.

## Check constraints

Repeat `[InquiryCheck("Quantity >= 0", Name = "CK_Product_Quantity")]` on an entity to emit named
table checks. Check expressions are raw provider SQL over physical column names; Inquiry neither quotes
nor translates them, so keep runtime input out and use syntax accepted by every provider you target.

## Foreign-key actions

`[InquiryForeignKey]` accepts `ConstraintName`, `OnDelete`, and `OnUpdate`. The action enum provides
`NoAction`, `Restrict`, `Cascade`, `SetNull`, and `SetDefault`; unsupported provider/action pairs are
compile-time errors. `SetNull` requires a nullable local property, and `SetDefault` requires a mapped
database default.

| Capability | SQLite | SQL Server | PostgreSQL | MySQL | MariaDB | Oracle |
|---|---|---|---|---|---|---|
| Covering `Include` | — | Yes | Yes | — | — | — |
| Checks | Yes | Yes | Yes | Yes | Yes | Yes |
| Delete `Cascade` / `SetNull` | Yes | Yes | Yes | Yes | Yes | Yes |
| Delete `SetDefault` | Yes | Yes | Yes | — | — | — |
| Update actions | Yes | Yes, except `Restrict` | Yes | Yes, except `SetDefault` | Yes, except `SetDefault` | — |

Oracle supports delete `Cascade` and `SetNull` only. SQL Server and Oracle do not accept `Restrict`;
Inquiry does not rewrite it to `NoAction` because their timing semantics are not universally equivalent.

## DDL safety lints (opt-in)

Inquiry ships advisory analyzer "lints" for risky schema shapes. They are **off by default** — turn them on in `.editorconfig` per ID when you want them:

```ini
# Surface a lint as a warning (or info/error) — opt in per ID
dotnet_diagnostic.INQ061.severity = warning
dotnet_diagnostic.INQ062.severity = warning
dotnet_diagnostic.INQ064.severity = warning
dotnet_diagnostic.INQ066.severity = warning
dotnet_diagnostic.INQ067.severity = warning
```

| ID | Lints | Why |
|---|---|---|
| **`INQ061`** | A foreign-key column with no index. | Most engines (SQL Server, PostgreSQL, Oracle, SQLite) don't auto-index foreign keys, so joins and `ON DELETE/UPDATE` cascades over the column scan the table. Add `IsIndexed = true` to the column's `[InquiryColumn]` / `[InquiryForeignKey]`. **MySQL/InnoDB auto-indexes FK constraints and is exempt.** |
| **`INQ062`** | A `decimal` column with no explicit precision/scale. | It silently takes the dialect default (e.g. `DECIMAL(18,2)`), which can round — a real hazard for money. Set `[InquiryColumn(Precision = …, Scale = …)]` (or `SqlType`). EF Core's `DecimalTypeDefaultWarning` is the same advisory. |
| **`INQ064`** | A column a store method filters on (a `[InquirySelectAllByField]` field or an `[InquiryWhere]` criterion) with no index. | Those queries scan the table. Add `[InquiryColumn(IsIndexed = true)]` (or `IsUnique`) to a column you filter often. |
| **`INQ066`** | A nullable column with a `DefaultExpression`. | New rows always receive the default, so `NULL` is unreachable via `INSERT` — the nullable + default pairing is usually unintentional. Either make the column `NOT NULL`, or remove the default if `NULL` is meaningful. |
| **`INQ067`** | A `string` column with no explicit `Length` or `SqlType`. | It takes the dialect's unbounded text type (`TEXT`, `NVARCHAR(MAX)`, `CLOB`, etc.), which may inhibit indexing or bloat row storage. Set `[InquiryColumn(Length = …)]` (or `SqlType`) for a bounded type. |

Because they're off by default, the lints never break a build until you opt in — then they participate in `dotnet build` (and CI) at the severity you choose, just like any analyzer diagnostic.

## What it isn't

This is not a full migration framework. It's a **CREATE-from-scratch** script. For evolving an existing schema (add column, drop table, etc.), use a dedicated migration tool (DbUp, FluentMigrator, EF Migrations, Flyway) and treat `InquiryGeneratedSchema.Ddl` as the canonical reference for what the live schema should look like.
