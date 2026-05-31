# E1 — MySQL / MariaDB Provider

> Workstream spec for parallel execution. See [README.md](README.md) for the dependency graph and Phase 0 foundation.
> Depends on: **F8** (provider append-point convention). Must implement any new `SqlBuilder` abstract member added by sibling workstreams (prefer F3 `virtual`+default). Size: **M**. Contention: **LOW** (append-only shared edits + zero shared-abstraction/runtime changes).

## 1. Feature summary & user-facing surface
- New package `Inquiry.MySql` (runtime) + `Inquiry.MySql.Analyzer`, mirroring the existing three-provider split.
- Register: `services.AddInquiry().AddInquiryMySql(connectionString);` optional `[assembly: InquiryDialect("MySql")]` to disambiguate.
- ADO.NET provider: **MySqlConnector** (MIT, fully async) — NOT Oracle's GPL `MySql.Data`. MariaDB is wire-compatible → one provider serves both.
- All existing attributes/store methods work unchanged. The only nuance: `ReturnEntity = true` is emulated (see §2) since MySQL lacks `RETURNING` (and even MariaDB lacks `UPDATE … RETURNING`).

## 2. Design
New projects (clone `Inquiry.Sqlite` / `Inquiry.Sqlite.Analyzer` shapes):
- `src/Inquiry.MySql/` — `Inquiry.MySql.csproj` (multi-target, bundles analyzer + Generators.Shared, refs `MySqlConnector`), `AssemblyInfo.cs` → `[assembly: InquiryDialect("MySql")]`, `MySqlInquiryConnectionFactory.cs`, `DependencyInjection/MySqlInquiryServiceCollectionExtensions.cs`.
- `src/Inquiry.MySql.Analyzer/` — `InquiryMySqlGenerator.cs` (`Dialect => "MySql"`, `CreateSqlBuilder() => new MySqlSqlBuilder()`), `MySqlSqlBuilder.cs`.

`MySqlSqlBuilder` (subclass of `SqlBuilder`; `SqlBuildContext` already precomputes most fragments using `@name` params which MySqlConnector accepts):
- `DialectName => "MySql"`; keep base `@name` param prefix.
- `QuoteIdentifier(id)` → backtick `` `id` `` with `` ` `` doubled.
- Select/update/delete: identical structure to `SqliteSqlBuilder`.
- `BuildInsertSql` empty-insertable case: `INSERT INTO t () VALUES ()` (MySQL has no `DEFAULT VALUES`).
- `BuildUpsertSql`: `INSERT … ON DUPLICATE KEY UPDATE col = VALUES(col), …` (use `VALUES(col)` form for MySQL 5.7/MariaDB compat; accept the 8.0.20 deprecation). Generated-key path is native (no IF/MERGE needed). `SetClauses` already excludes keys/generated.

**Returning path (no `RETURNING`)** — emit a **two-statement batch** ending in a `SELECT`, so the runtime needs ZERO changes (it already runs multi-statement batches for SqlServer MERGE / PG CTE and reads the first row):
- Insert-returning, generated key: `INSERT …; SELECT cols FROM t WHERE key = LAST_INSERT_ID()` (session-scoped, safe — pipeline opens a dedicated connection per call).
- Insert-returning, client key: `INSERT …; SELECT cols FROM t WHERE key = @Key`.
- Update-returning: `UPDATE …; SELECT cols FROM t WHERE keywhere`.
- Upsert-returning: the existing emitter splits null-key→insert-returning (`LAST_INSERT_ID()`) vs non-null→`SELECT … WHERE key = @Key`. No emitter change needed.
- Always emit the emulated form (don't branch on server version) — correct on MySQL + all MariaDB versions, keeps the dialect string stable.

Generator wiring: none new — `InquiryGeneratorBase` arbitration already handles a new dialect via `Dialect => "MySql"`.

## 3. Per-dialect notes
Backtick quoting (identical MySQL/MariaDB); schema==database in `QuoteTable`; `ON DUPLICATE KEY UPDATE` fires on ANY unique conflict (document); `AUTO_INCREMENT` + session-scoped `LAST_INSERT_ID()`; `bool`→`TINYINT(1)`; `Guid` keys need explicit `GuidFormat` in connection string; string PKs need bounded `VARCHAR(n)` (can't index `LONGTEXT`).

## 4. Implementation steps (TDD)
1. Add `MySqlConnector` to `Directory.Packages.props`. *Verify:* restore.
2. Create analyzer project + `MySqlSqlBuilder` stubs + `InquiryMySqlGenerator`; add both to `Inquiry.slnx`. *Verify:* solution compiles.
3. **Generator emission test first** in `InquiryGeneratorTests.cs` (model on `SqlServerDialectEmits…`): register `new InquiryMySqlGenerator()` in the `generators[]` array + add the MySql runtime assembly to `GetReferences()`. Assert backtick identifiers, `ON DUPLICATE KEY UPDATE … VALUES()`, and the `; SELECT` returning batches (both `LAST_INSERT_ID()` and `@Key` forms). *Verify:* red → green.
4. Implement `MySqlSqlBuilder` until step-3 passes.
5. Implement runtime project (factory, DI, AssemblyInfo). *Verify:* build.
6. Add `MySqlDdl` to `NorthwindSchema.cs` (13 tables, backtick, `AUTO_INCREMENT`, `VARCHAR(n)` keys, `LONGTEXT`/`LONGBLOB`, `CREATE TABLE IF NOT EXISTS`).
7. Create `tests/Inquiry.MySql.Tests` with `MySqlFactAttribute` + `MySqlTestHarness` (env var `INQUIRY_MYSQL_CONNECTION_STRING`), cloning the PostgreSql test project. *Verify:* tests skip when env unset.
8. Run integration suite against **both** MySQL and MariaDB containers. *Verify:* returning, upsert, composite keys, eager loading pass on both.

## 5. Shared-file contention map
- **MODIFY (append-only, low conflict):** `Directory.Packages.props`, `Inquiry.slnx`, `samples/Inquiry.Northwind/NorthwindSchema.cs` (add `MySqlDdl`), `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs` (generator array + references — **same lines E2/Oracle edits**, coordinate).
- **ADD (no contention):** everything under `src/Inquiry.MySql/`, `src/Inquiry.MySql.Analyzer/`, `tests/Inquiry.MySql.Tests/`.
- **Shared abstractions / runtime: NONE.** Fits entirely inside the existing `SqlBuilder` contract + pipeline. This is the key parallelization win.

## 6. Cross-workstream dependencies
- Blocks on: nothing (start immediately; only F8 convention helps). Rebase to implement any new `SqlBuilder` member added by W1/W2/W5/W7/W8/W9.
- Coordinate `InquiryGeneratorTests.cs` generator-array edits with **E2 Oracle** (same lines).
- Enables **E3** Aurora-MySQL.
- If a workstream adds an **abstract** `SqlBuilder` member, this provider must implement it (else build breaks) — argues for F3 `virtual`+default.

## 7. Test strategy
Generator emission tests (always-on, no DB) for exact const SQL. Opt-in integration via `MySqlFactAttribute` (skips without env var). CI: separate opt-in job with **both** a MySQL and a MariaDB service container (divergent RETURNING/upsert edge behavior).

## 8. Risks / open questions
1. **Multi-statement support** for the `; SELECT` returning batch — default-on in MySqlConnector but disabled by PlanetScale / `AllowUserVariables=false`. Document; fallback (second `DbCommand`) would need a runtime change → prefer batch.
2. **Result-set sequencing (highest risk):** confirm the reader surfaces the trailing `SELECT` as the first consumable result set over a multi-statement batch (the pipeline reads result set #1). SqlServer's existing IF/INSERT/OUTPUT upsert relies on the same pattern (evidence it holds), but verify with a spike before implementing all builder methods. If not, emitter needs `NextResult` handling.
3. `ON DUPLICATE KEY` fires on any unique conflict — document.
4. `VALUES(col)` deprecation (8.0.20+) — still functional; chosen for MariaDB/older-MySQL compat.

## 9. Size: **M** — faithful clone of the 3-provider template + the emulated-returning spike + 13-table DDL + dual-engine CI. No shared/runtime changes.
