# Test Coverage & Benchmark Expansion — Implementation Plan

> **✅ COMPLETE & MERGED TO `main` (2026-06-02).** All tasks below were implemented and merged; the
> resulting coverage and benchmark state is reconciled in [`docs/STATUS.md`](../../STATUS.md) §1 and §3.
> A follow-up — replicating the *full* Northwind surface across all four stacks in tests and benchmarks —
> is tracked in STATUS.md §3.G #18. The unchecked `- [ ]` boxes below are retained as the historical
> implementation record; treat STATUS.md as the live progress source.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring all four live dialects (PostgreSQL, SQL Server, MySQL, Oracle) to a uniform feature-test matrix and expand the benchmarks to a 100k-row tier covering Inquiry's important added features.

**Architecture:** A shared, linked "feature catalog" (entities + per-dialect `FeatureSchema` DDL) gives every dialect test project live coverage of W6/W8/W10 without touching the canonical Northwind schema; projections/aggregations/batch are added as store methods on existing Northwind stores; FTS source is isolated from Sqlite-compiled projects. Benchmarks gain `[Params(1000, 100000)]` and new operation classes.

**Tech Stack:** .NET 10 SDK (multi-TFM net6–net10), xUnit + Xunit.SkippableFact, Testcontainers (PG/MySQL/SQL Server/Oracle), BenchmarkDotNet, Inquiry compile-time source generators.

**Spec:** [`docs/superpowers/specs/2026-06-02-test-coverage-and-benchmark-expansion-design.md`](../specs/2026-06-02-test-coverage-and-benchmark-expansion-design.md)

**Conventions (from STATUS.md §2):** TDD — red generator-emission test (assert exact emitted `const string`) → implement → integration test. Commit messages via BOM-free file + `git commit -F`, ending with the `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer. Work on a feature branch; merge to `main` when green + reviewed.

---

## File Structure

**New shared source — `tests/Inquiry.FeatureCatalog/`** (plain `.cs` files, no `.csproj`; linked into test projects):
- `VersionedItem.cs` — W6 entity + `VersionedItemStore` (`[InquiryConcurrencyToken]`).
- `SoftItem.cs` — W8 entity + `SoftItemStore` (`[InquirySoftDelete]`, restore, count, hard-delete).
- `JsonDoc.cs` — W10 entity + `JsonDocStore` + `Money`/`MoneyConverter` (`Converter=`, `[InquiryJson]`).
- `FeatureSchema.cs` — `SqliteDdl`/`PostgreSqlDdl`/`SqlServerDdl`/`MySqlDdl`/`OracleDdl` for the three entities above.
- `FullText/Article.cs` — W9 entity + `ArticleStore` (`[InquiryFullTextSearch]`). **Isolated.**
- `FullText/FullTextSchema.cs` — `PostgreSqlDdl`/`SqlServerDdl`/`MySqlDdl` (incl. full-text indexes). **No Sqlite/Oracle.**

**Modified shared Northwind source** (`samples/Inquiry.Northwind/Stores/`):
- `ProductStore.cs` — add `[InquiryCount]`, `[InquiryAggregate]` (Max/Min/Sum/Avg), projection method + `ProductSummary` record.
- `RegionStore.cs` — add `[InquiryInsertAll]`, `[InquiryUpdateAll]` (already has `[InquiryDeleteAll]`).

**Modified csproj wiring** (each test project): link the catalog; the 4 non-Sqlite ones additionally link `FullText/`.

**New integration test files** (per dialect — mirror established patterns):
- PostgreSQL & SQL Server: `PredicateSelectIntegrationTests.cs`, `PaginationIntegrationTests.cs`, `BatchIntegrationTests.cs`.
- All four: `AggregateIntegrationTests.cs`, `ProjectionIntegrationTests.cs`, `ConcurrencyIntegrationTests.cs`, `SoftDeleteIntegrationTests.cs`, `ConverterIntegrationTests.cs`.
- PG/MySQL/SQL Server: `FullTextSearchIntegrationTests.cs` (SQL Server env-gated).
- Oracle: `MultiColumnPredicateIntegrationTests.cs` + self-ref FK fill-in (add to existing coverage file).

**New generator-emission tests** (`tests/Inquiry.Generators.Tests/`):
- Extend `AggregateGeneratorTests.cs` / `ProjectionGeneratorTests.cs` / `BatchInsertGeneratorTests.cs` for the new Northwind store methods if not already covered by the generic ones.

**New/modified benchmarks** (`benchmarks/Inquiry.Benchmarks/`):
- `BenchmarkDatabase.cs` — `SeedRows` becomes a parameter; batched 100k seeding.
- `CustomerCrudBenchmarks.cs` / `ProductCrudBenchmarks.cs` / `ShipperCrudBenchmarks.cs` — add `[Params(1000, 100000)]`.
- New: `PaginationBenchmarks.cs`, `BatchBenchmarks.cs`, `ProjectionBenchmarks.cs`, `PredicateBenchmarks.cs`, `EagerLoadingBenchmarks.cs`.
- `CrossDialectReadBenchmarks.cs` — add the 100k tier.
- `README.md` — document new params/classes.

---

## Phase 0 — Foundation (serialized, in-session)

### Task 0: Feature branch + confirm baseline

- [ ] **Step 1:** Create branch.
```bash
git checkout -b feature/test-coverage-and-bench-expansion
```
- [ ] **Step 2:** Confirm non-Docker baseline green (already verified: Generators/Runtime/SQLite pass on net8.0). Note counts for the STATUS.md update at the end.

---

### Task 1: Feature catalog entities + stores

**Files:** Create `tests/Inquiry.FeatureCatalog/VersionedItem.cs`, `SoftItem.cs`, `JsonDoc.cs`.

- [ ] **Step 1: Write the entities/stores** (APIs verified against existing SQLite fixtures).

`VersionedItem.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("VersionedItem")]
public sealed class VersionedItem
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn("Title")] public string Title { get; set; } = string.Empty;
    [InquiryConcurrencyToken] public int Version { get; set; }
}

public partial class VersionedItemStore : InquiryStore<VersionedItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<VersionedItem?> InsertAsync(VersionedItem item, CancellationToken cancellationToken = default);
    [InquirySelectOneByKey]
    public partial Task<VersionedItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);
    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(VersionedItem item, CancellationToken cancellationToken = default);
    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteAsync(VersionedItem item, CancellationToken cancellationToken = default);
}
```

`SoftItem.cs`:
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

[InquiryTable("SoftItem")]
public sealed class SoftItem
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;
    [InquiryColumn("IsDeleted"), InquirySoftDelete] public bool IsDeleted { get; set; }
}

public partial class SoftItemStore : InquiryStore<SoftItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<SoftItem?> InsertAsync(SoftItem item, CancellationToken cancellationToken = default);
    [InquirySelectAll]
    public partial Task<IReadOnlyList<SoftItem>> AllAsync(CancellationToken cancellationToken = default);
    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<SoftItem>> AllIncludingDeletedAsync(CancellationToken cancellationToken = default);
    [InquirySelectOneByKey]
    public partial Task<SoftItem?> ByIdAsync(long id, CancellationToken cancellationToken = default);
    [InquiryDeleteOneByKey]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);
    [InquiryDeleteOneByKey(HardDelete = true)]
    public partial Task<bool> PurgeAsync(long id, CancellationToken cancellationToken = default);
    [InquiryRestoreOneByKey]
    public partial Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);
    [InquiryCount]
    public partial Task<long> CountActiveAsync(CancellationToken cancellationToken = default);
}
```

`JsonDoc.cs` (use text-compatible columns; native jsonb/json column types are out of scope to avoid driver-cast issues — see spec §6):
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

public readonly struct Money { public decimal Amount { get; init; } }

public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
{
    public decimal ToProvider(Money model) => model.Amount;
    public Money FromProvider(decimal provider) => new() { Amount = provider };
}

[InquiryTable("JsonDoc")]
public sealed class JsonDoc
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn("Owner")] public string Owner { get; set; } = string.Empty;
    [InquiryColumn("Balance", Converter = typeof(MoneyConverter))] public Money Balance { get; set; }
    [InquiryColumn("Tags"), InquiryJson] public List<string>? Tags { get; set; }
}

public partial class JsonDocStore : InquiryStore<JsonDoc>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(JsonDoc doc, CancellationToken cancellationToken = default);
    [InquirySelectOneByKey]
    public partial Task<JsonDoc?> GetAsync(long id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2:** (No build yet — needs csproj wiring in Task 3.) Commit after Task 3 builds.

---

### Task 2: FeatureSchema DDL (per dialect)

**Files:** Create `tests/Inquiry.FeatureCatalog/FeatureSchema.cs`.

- [ ] **Step 1: Write per-dialect DDL** for the three tables. Respect dialect types: identity, bool, and string lengths. (Tags/Owner kept text-compatible; Balance numeric.)

```csharp
namespace Inquiry.FeatureCatalog;

public static class FeatureSchema
{
    public const string SqliteDdl = """
        CREATE TABLE VersionedItem (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE SoftItem (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsDeleted INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE JsonDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Owner TEXT NOT NULL, Balance NUMERIC NOT NULL, Tags TEXT NULL);
        """;

    public const string PostgreSqlDdl = """
        CREATE TABLE VersionedItem (Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Title TEXT NOT NULL, "Version" INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE SoftItem (Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Name TEXT NOT NULL, IsDeleted BOOLEAN NOT NULL DEFAULT FALSE);
        CREATE TABLE JsonDoc (Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Owner TEXT NOT NULL, Balance NUMERIC NOT NULL, Tags TEXT NULL);
        """;

    public const string SqlServerDdl = """
        CREATE TABLE VersionedItem (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Title NVARCHAR(200) NOT NULL, [Version] INT NOT NULL DEFAULT 0);
        CREATE TABLE SoftItem (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(200) NOT NULL, IsDeleted BIT NOT NULL DEFAULT 0);
        CREATE TABLE JsonDoc (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Owner NVARCHAR(200) NOT NULL, Balance DECIMAL(18,2) NOT NULL, Tags NVARCHAR(MAX) NULL);
        """;

    public const string MySqlDdl = """
        CREATE TABLE VersionedItem (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Title VARCHAR(200) NOT NULL, Version INT NOT NULL DEFAULT 0);
        CREATE TABLE SoftItem (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Name VARCHAR(200) NOT NULL, IsDeleted TINYINT(1) NOT NULL DEFAULT 0);
        CREATE TABLE JsonDoc (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Owner VARCHAR(200) NOT NULL, Balance DECIMAL(18,2) NOT NULL, Tags VARCHAR(2000) NULL);
        """;

    // Oracle: identity columns (12c+), NUMBER(1) bool, quoted "Version" (reserved). One statement per
    // string at execution time (Oracle rejects batched DDL) — the Oracle harness already splits on ';'.
    public const string OracleDdl = """
        CREATE TABLE VersionedItem (Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Title VARCHAR2(200) NOT NULL, "Version" NUMBER(10) DEFAULT 0 NOT NULL);
        CREATE TABLE SoftItem (Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Name VARCHAR2(200) NOT NULL, IsDeleted NUMBER(1) DEFAULT 0 NOT NULL);
        CREATE TABLE JsonDoc (Id NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Owner VARCHAR2(200) NOT NULL, Balance NUMBER(18,2) NOT NULL, Tags VARCHAR2(2000) NULL);
        """;
}
```

- [ ] **Step 2: Verify the Oracle harness splits multi-statement DDL.** Read `tests/Inquiry.Oracle.Tests/Fixtures/OracleTestHarness.cs`. If `CreateFromDdlAsync` does not split on `;`, the Oracle feature DDL must be applied statement-by-statement — adapt the harness call or pass statements individually. (The existing `GeneratedDdlIntegrationTests` already splits Oracle DDL; reuse that mechanism.)

---

### Task 3: Wire the catalog into all five test projects

**Files:** Modify the 5 `tests/Inquiry.*.Tests/Inquiry.*.Tests.csproj`.

- [ ] **Step 1:** In **each** of the 5 test csproj `<ItemGroup>` that links Northwind, add the catalog (top-level only):
```xml
    <Compile Include="..\Inquiry.FeatureCatalog\*.cs" LinkBase="FeatureCatalog" />
```
- [ ] **Step 2:** In the **four non-Sqlite** test csproj only (PG/SQL Server/MySQL/Oracle), also add:
```xml
    <Compile Include="..\Inquiry.FeatureCatalog\FullText\*.cs" LinkBase="FeatureCatalog\FullText" />
```
(Oracle inclusion is provisional — removed in Task 9 if Oracle FTS is unsupported.)
- [ ] **Step 3: Build each test project** to confirm the catalog compiles under every dialect.
```bash
dotnet build tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0
dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0
dotnet build tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0
dotnet build tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0
```
Expected: all succeed (FTS not yet added, so no INQ035). The Sqlite project does **not** include `FullText/` so it won't hit INQ035.
- [ ] **Step 4: Commit.** `feat(test): add shared feature catalog (W6/W8/W10 entities + per-dialect DDL)`

---

### Task 4: W5 + W3 store methods on Northwind (red emission → green)

**Files:** Modify `samples/Inquiry.Northwind/Stores/ProductStore.cs`, `RegionStore.cs`; add tests in `tests/Inquiry.Generators.Tests/`.

- [ ] **Step 1: Add the new store methods.**

`ProductStore.cs` — append before the closing brace:
```csharp
    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Max, "UnitPrice")]
    public partial Task<decimal?> MaxUnitPriceAsync(CancellationToken cancellationToken = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "UnitsInStock")]
    public partial Task<long?> TotalUnitsInStockAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<ProductSummary>> SummariesAsync(CancellationToken cancellationToken = default);
```
Add the projection record (new file `samples/Inquiry.Northwind/Models/ProductSummary.cs`):
```csharp
using Inquiry.Entities;
using Inquiry.Northwind.Models;

namespace Inquiry.Northwind.Models;

[InquiryProjection(typeof(Product))]
public sealed record ProductSummary
{
    [InquiryColumn("ProductID")] public int? ProductID { get; init; }
    [InquiryColumn("ProductName")] public string ProductName { get; init; } = string.Empty;
    [InquiryColumn("UnitPrice")] public decimal? UnitPrice { get; init; }
}
```
`RegionStore.cs` — add batch insert/update:
```csharp
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<Region> regions, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Verify the Northwind sample still builds under Sqlite** (its own dialect) — this is the red/green for the generator (a generation error fails the build here).
```bash
dotnet build samples/Inquiry.Northwind/Inquiry.Northwind.csproj -f net8.0
```
Expected: PASS. If any aggregate/projection/batch attribute is misused, the generator emits a diagnostic and the build fails — fix before proceeding.
- [ ] **Step 3: Add/extend emission tests** asserting per-dialect SQL for the new methods. Check whether `AggregateGeneratorTests`/`ProjectionGeneratorTests`/`BatchInsertGeneratorTests` already cover these generically; if the generic coverage is sufficient (they use synthetic entities), add **one** focused test per new shape only if a gap exists. Run:
```bash
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -f net8.0
```
Expected: PASS (160+ tests).
- [ ] **Step 4: Build all 5 test projects** (they link the modified Northwind) to confirm the new methods generate on every dialect.
- [ ] **Step 5: Commit.** `feat(northwind): add W5 aggregate/projection + W3 batch store methods`

---

### Task 5: FTS catalog source (isolated)

**Files:** Create `tests/Inquiry.FeatureCatalog/FullText/Article.cs`, `FullText/FullTextSchema.cs`.

- [ ] **Step 1: Write the FTS entity/store.**
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.FullText;

[InquiryTable("Article")]
public sealed class Article
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn("Title")] public string Title { get; set; } = string.Empty;
    [InquiryColumn("Body")] public string Body { get; set; } = string.Empty;
}

public partial class ArticleStore : InquiryStore<Article>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Article?> InsertAsync(Article article, CancellationToken cancellationToken = default);
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Article>> AllAsync(CancellationToken cancellationToken = default);
    [InquiryFullTextSearch("Title", "Body")]
    public partial Task<IReadOnlyList<Article>> SearchAsync(string term, CancellationToken cancellationToken = default);
}
```
- [ ] **Step 2: Write `FullTextSchema.cs`** with full-text indexes.
```csharp
namespace Inquiry.FeatureCatalog.FullText;

public static class FullTextSchema
{
    // PostgreSQL: the generated query uses to_tsvector(...) @@ plainto_tsquery(...); a seq scan is correct
    // for small test data, so no GIN index is required.
    public const string PostgreSqlDdl = """
        CREATE TABLE Article (Id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, Title TEXT NOT NULL, Body TEXT NOT NULL);
        """;

    // MySQL: MATCH...AGAINST REQUIRES a FULLTEXT index (InnoDB supports it on 8.0).
    public const string MySqlDdl = """
        CREATE TABLE Article (Id BIGINT AUTO_INCREMENT PRIMARY KEY, Title VARCHAR(400) NOT NULL, Body TEXT NOT NULL, FULLTEXT KEY ft_article (Title, Body)) ENGINE=InnoDB;
        """;

    // SQL Server: FREETEXT requires a full-text catalog + index AND the full-text component installed in
    // the engine. The base Linux container often lacks it; the test is env-gated and skips with a clear
    // reason when CREATE FULLTEXT fails.
    public const string SqlServerDdl = """
        CREATE TABLE Article (Id BIGINT IDENTITY(1,1) PRIMARY KEY, Title NVARCHAR(400) NOT NULL, Body NVARCHAR(MAX) NOT NULL);
        """;
    public const string SqlServerFullTextSetup = """
        CREATE FULLTEXT CATALOG ft_catalog AS DEFAULT;
        CREATE FULLTEXT INDEX ON Article (Title, Body) KEY INDEX PK__Article;
        """; // PK index name is resolved at runtime; see the SQL Server FTS test for the lookup.
}
```
- [ ] **Step 3: Build the 4 non-Sqlite test projects.** PG/SQL Server/MySQL must pass. **Oracle:** if the build fails with an unsupported-FTS diagnostic, that confirms Oracle FTS is unimplemented → proceed to Task 9 (remove FTS from Oracle csproj + document gap). If it builds, keep Oracle FTS provisional.
```bash
dotnet build tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0
dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0
dotnet build tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0
dotnet build tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -f net8.0
```
- [ ] **Step 4: Commit.** `feat(test): add isolated FTS catalog (Article + per-dialect full-text DDL)`

---

## Phase 1 — Live integration tests (parallelizable per dialect/feature)

> Each test file mirrors an existing reference file, swapping the harness type and DDL. Reference files:
> predicate/pagination → `tests/Inquiry.MySql.Tests/{PredicateSelectIntegrationTests,PaginationIntegrationTests}.cs`;
> feature tests → the SQLite originals (`tests/Inquiry.Sqlite.Tests/{Concurrency,SoftDelete,Converter,Aggregate,Projection}IntegrationTests.cs`).
> All live tests use `[SkippableFact]` + `Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason)` and the
> `[Collection(<Dialect>Collection.Name)]` fixture pattern. Feature tests build the harness with
> `<Dialect>TestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.<Dialect>Ddl, "<prefix>")`.

### Task 6: W1/W2 parity — PostgreSQL & SQL Server

**Files:** Create `tests/Inquiry.PostgreSql.Tests/PredicateSelectIntegrationTests.cs`, `PaginationIntegrationTests.cs`; same two in `tests/Inquiry.SqlServer.Tests/`.

- [ ] **Step 1:** Copy MySQL's `PredicateSelectIntegrationTests.cs`; change namespace to the target, `MySqlContainerFixture`→`PostgreSqlContainerFixture` (resp. SqlServer), `MySqlCollection`→`PostgreSqlCollection`, `MySqlTestHarness`→`PostgreSqlTestHarness`. Body (seed + assertions) is identical (store methods already exist on `ProductStore`).
- [ ] **Step 2:** Same for `PaginationIntegrationTests.cs`.
- [ ] **Step 3: Build** both projects (`-f net8.0`). Expected: PASS (compile).
- [ ] **Step 4: Commit.** `test(pg,mssql): add live W1 predicate + W2 pagination parity suites`
- [ ] **Step 5:** (Live run deferred to Phase 3 batch verification.)

### Task 7: W3 batch — PostgreSQL, SQL Server, MySQL

**Files:** Create `BatchIntegrationTests.cs` in the PG, SQL Server, and MySQL test projects.

- [ ] **Step 1:** Mirror Oracle's `BatchDeleteIntegrationTests.cs` for batch delete via `RegionStore.DeleteAllAsync`, and add batch insert/update via the new `RegionStore.InsertAllAsync`/`UpdateAllAsync`. Seed a handful of regions, assert affected counts and resulting rows; include the empty-collection no-op case.
- [ ] **Step 2: Build** the three projects. **Step 3: Commit.** `test(pg,mssql,mysql): add live W3 batch insert/update/delete suites`

### Task 8: W5/W6/W8/W10 — all four dialects

**Files:** Create `AggregateIntegrationTests.cs`, `ProjectionIntegrationTests.cs`, `ConcurrencyIntegrationTests.cs`, `SoftDeleteIntegrationTests.cs`, `ConverterIntegrationTests.cs` in each of the PG/SQL Server/MySQL/Oracle test projects (20 files).

- [ ] **Step 1 (Aggregates/Projections):** Use Northwind. Seed a few categories+products via the stores, then assert `ProductStore.CountAsync`, `MaxUnitPriceAsync`, `TotalUnitsInStockAsync`, and `SummariesAsync` (projection subset). Mirror `tests/Inquiry.Sqlite.Tests/{Aggregate,Projection}IntegrationTests.cs` logic against the dialect harness (standard `CreateAsync`, Northwind DDL).
- [ ] **Step 2 (Concurrency W6):** Mirror SQLite `ConcurrencyIntegrationTests.cs` using `VersionedItemStore` + `FeatureSchema.<Dialect>Ddl` via `CreateFromDdlAsync`. Cover: version bumps on update; stale update → `false`; stale delete → `false`; and `ThrowOnConcurrencyConflict` → `InquiryConcurrencyException` (rebuild a throwing service provider as the SQLite test does, using `AddInquiry<Dialect>(harness.ConnectionString)`).
- [ ] **Step 3 (SoftDelete W8):** Mirror SQLite `SoftDeleteIntegrationTests.cs` using `SoftItemStore`: soft-delete hides/`IncludeDeleted` shows, restore, hard-delete (`Purge`), `CountActiveAsync`.
- [ ] **Step 4 (Converter/JSON W10):** Mirror SQLite `ConverterIntegrationTests.cs` using `JsonDocStore`: Money converter + JSON `Tags` round-trip + null-Tags→NULL. For the raw-text assertion, use each harness's scalar helper (add a small `ExecuteScalarAsync` to a harness if absent, mirroring the SQLite one).
- [ ] **Step 5: Build** all four projects. **Step 6: Commit** per dialect or per feature group. `test(<dialect>): add live W5/W6/W8/W10 feature suites`

### Task 9: W9 FTS — PG/MySQL (+ SQL Server gated) and Oracle decision

**Files:** Create `FullTextSearchIntegrationTests.cs` in PG, MySQL, SQL Server test projects.

- [ ] **Step 1 (PG, MySQL):** Build harness with `CreateFromDdlAsync(admin, FullTextSchema.<Dialect>Ddl, "fts")`; seed 3–4 articles; assert `ArticleStore.SearchAsync("term")` returns the expected matches (choose terms that exercise natural-language matching). For MySQL, after inserting, the FULLTEXT index is maintained automatically.
- [ ] **Step 2 (SQL Server, gated):** Attempt `CreateFullTextCatalog`+index in a `try/catch`; if it throws (component absent), `Skip` with reason `"SQL Server full-text component not installed in container"`. Resolve the PK index name via `sys.indexes` for the `KEY INDEX` clause. On success, assert `SearchAsync` matches.
- [ ] **Step 3 (Oracle):** Apply the Task 5 build result. If Oracle FTS is unsupported, **remove** the `FullText\*.cs` link from `tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj`, and add a generator-emission test asserting the Oracle dialect rejects/does-not-support `[InquiryFullTextSearch]` (mirror `SqliteRejectsFullTextSearchWithInq035`, adjusting for whatever diagnostic Oracle emits). Document the gap in STATUS.md (Task 12).
- [ ] **Step 4: Build** affected projects. **Step 5: Commit.** `test(pg,mysql,mssql): add live W9 full-text search; document Oracle FTS gap`

### Task 10: Oracle parity gap-fills

**Files:** Modify `tests/Inquiry.Oracle.Tests/NorthwindCoverageIntegrationTests.cs` (or add a focused file).

- [ ] **Step 1:** Add a multi-column WHERE predicate test (`OrderStore.SelectByCustomerAndEmployee`-style — verify the exact Oracle store method name; the field-pair `[InquirySelectAllByField("CustomerID","EmployeeID")]` exists on `OrderStore`) and an `Employee.ReportsTo` self-referential FK round-trip, mirroring the PG/SQL Server/MySQL `NorthwindCoverageIntegrationTests` cases.
- [ ] **Step 2: Build. Step 3: Commit.** `test(oracle): add multi-column WHERE + self-ref FK parity`

---

## Phase 2 — Benchmarks

### Task 11: Parameterize dataset size + add operation benchmarks

**Files:** Modify `benchmarks/Inquiry.Benchmarks/BenchmarkDatabase.cs`, the three `*CrudBenchmarks.cs`, `CrossDialectReadBenchmarks.cs`, `README.md`; create `PaginationBenchmarks.cs`, `BatchBenchmarks.cs`, `ProjectionBenchmarks.cs`, `PredicateBenchmarks.cs`, `EagerLoadingBenchmarks.cs`.

- [ ] **Step 1: Make seeding size-parameterized.** In `BenchmarkDatabase.cs`, replace the `const int SeedRows = 1000` with a constructor/param-driven `int seedRows`, and batch the inserts (multi-row `INSERT` or a single transaction with a prepared command — 100k rows must seed in seconds, not minutes). Keep the seed deterministic.
- [ ] **Step 2: Add `[Params(1000, 100000)]`** to `CustomerCrudBenchmarks`, `ProductCrudBenchmarks`, `ShipperCrudBenchmarks` as a public field `public int Rows;` consumed by `[GlobalSetup]` to size the DB. Ensure point-read benchmarks (`SelectByKey`) target an existing row valid at both sizes; `SelectAll` now returns `Rows` rows.
- [ ] **Step 3: Smoke-test** at both tiers with a short job:
```bash
dotnet run -c Release --project benchmarks/Inquiry.Benchmarks -- --filter "*Customer*SelectByKey*" --job short
```
Expected: runs at Rows=1000 and Rows=100000, no errors.
- [ ] **Step 4: Add new benchmark classes** (in-process SQLite, Inquiry vs Dapper vs EF vs ADO.NET baseline, `MemoryDiagnoser`, `[CategoriesColumn]`):
  - `PaginationBenchmarks` — offset page (`ProductStore.PageByIdAsync`) and keyset page (`KeysetByIdAsync`) vs hand-written Dapper LIMIT/OFFSET + keyset.
  - `BatchBenchmarks` — `InsertAll`/`DeleteAll` (1000 rows) vs per-row loops in Dapper/ADO.
  - `ProjectionBenchmarks` — projection vs full-entity select; `Count`/`Sum` aggregate vs Dapper `ExecuteScalar`.
  - `PredicateBenchmarks` — `ProductStore.SearchAsync`/`InCategoriesAsync` vs parameterized Dapper.
  - `EagerLoadingBenchmarks` — `SelectAllWithCategoryAsync` (separate-query) vs Dapper multi-query.
  Each follows the structure of `ProductCrudBenchmarks.cs` (same DB harness, same competitor wiring). Use `[Params(1000, 100000)]` where the operation is read-shaped; keep batch at a fixed write size.
- [ ] **Step 5: Smoke-test each** new class with `--filter "*<Class>*" --job short`. Expected: PASS.
- [ ] **Step 6: Extend `CrossDialectReadBenchmarks`** with `[Params(1000, 100000)]` (the seeding loop already exists; make it size-driven). Smoke-test `--filter "*CrossDialect*SelectByKey*" --job short` (needs Docker).
- [ ] **Step 7: Update `README.md`** — document the params, the new classes, and 100k seeding. **Step 8: Commit.** `bench: parameterize dataset (1k/100k) + add pagination/batch/projection/predicate/eager benchmarks`

---

## Phase 3 — Verification & docs

### Task 12: Live verification (all four containers incl. Oracle) + doc updates

- [ ] **Step 1: Run non-Docker suites** (net8.0): Generators, Runtime, SQLite. Expected: all green, counts increased.
- [ ] **Step 2: Run PostgreSQL, SQL Server, MySQL live suites** (Docker). Confirm the new predicate/pagination/batch/feature/FTS tests execute (not skip) and pass.
```bash
dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0
dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0
```
- [ ] **Step 3: Run Oracle live suite** (heavy container; allow startup time). Confirm feature suites pass; FTS handled per Task 9.
```bash
dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -f net8.0
```
- [ ] **Step 4: Triage any failures** with superpowers:systematic-debugging. If a live failure reveals a real provider bug (not a test bug), fix it narrowly + add the regression note (spec §7). Re-run.
- [ ] **Step 5: Update `docs/STATUS.md`** — refresh the test-status table counts, add the new live-parity coverage to §3.B, and record the Oracle-FTS decision.
- [ ] **Step 6: Final full sweep** — `dotnet build Inquiry.slnx` + the suites once more; benchmarks compile in Release. **Step 7: Commit.** `docs(status): record expanded coverage + benchmark suite`

### Task 13: Code review + finish

- [ ] **Step 1:** Run superpowers:requesting-code-review on the branch diff; fix Critical/Important findings.
- [ ] **Step 2:** Use superpowers:finishing-a-development-branch to merge to `main` (project merges directly, no PR).

---

## Self-Review (planner)

- **Spec coverage:** A→Task 6/7/10; B(W5)→Task 4/8; C(W6/W8/W10)→Task 1/2/3/8; D(W9)→Task 5/9; E→Task 11. Verification→Task 12. All spec §4 workstreams have tasks. ✓
- **Type consistency:** Entity/store/record names (`VersionedItem`/`VersionedItemStore`, `SoftItem`/`SoftItemStore`, `JsonDoc`/`JsonDocStore`/`Money`/`MoneyConverter`, `Article`/`ArticleStore`, `ProductSummary`) and DDL table names match across tasks. `FeatureSchema.<Dialect>Ddl` / `FullTextSchema.<Dialect>Ddl` naming consistent. ✓
- **Placeholder scan:** Concrete code for all novel source; per-dialect tests reference exact mirror files + verified APIs (not "similar to"). Remaining runtime-verified specifics (Oracle DDL split, SQL Server PK index name, Oracle FTS support) are explicit verify-steps with defined fallbacks, not placeholders. ✓
- **Risk:** SQL Server FTS + Oracle FTS are the two genuine unknowns; both have defined skip/document fallbacks so they cannot block the plan.
