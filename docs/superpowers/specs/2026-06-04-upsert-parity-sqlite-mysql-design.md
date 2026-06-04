# Design — Upsert parity tests (Phase 2: SQLite + MySQL)

- **Date:** 2026-06-04
- **Status:** Approved (scope confirmed via scoping questions — see §2).
- **Owner:** in-session work, project process.

## 1. Goal

Phase 2 of the all-engine upsert-parity effort (see
[`2026-06-04-generated-key-upsert-atomicity-design.md`](2026-06-04-generated-key-upsert-atomicity-design.md)
§7). **Prove** SQLite + MySQL generated-key upsert behavior with explicit tests so parity across all
engines is demonstrated, not assumed. **Tests-only — no SQL builder changes** (both are already atomic:
SQLite `INSERT … ON CONFLICT`, MySQL `INSERT … ON DUPLICATE KEY UPDATE`).

## 2. Scope decisions (from scoping questions)

- **MySQL — full parity:** a GUID-default key via `DEFAULT (UUID())` + a 10-way concurrency test (MySQL is
  networked and its `ON DUPLICATE KEY UPDATE` is atomic).
- **SQLite — generated-key upsert, sequential:** a generated-`INTEGER` (rowid) key — null-key generates,
  explicit-key insert-then-update. **No** parallel-race test (SQLite is single-writer / in-process; a
  parallel test would be `SQLITE_BUSY` noise, not a meaningful atomicity proof) and **no** GUID-default
  (SQLite has no GUID type and no UUID-generating default).
- No builder changes.

## 3. Current state

- **MySQL:** `Guid` → `CHAR(36)` (`MySqlSqlBuilder.MapColumnType`); generated-key upsert is
  `INSERT … ON DUPLICATE KEY UPDATE` (atomic); harness `MySqlTestHarness.CreateFromDdlAsync(adminConn, ddl)`;
  container `mysql:8.4` (supports `DEFAULT (UUID())`, an 8.0.13+ feature).
- **SQLite:** `Guid` → `TEXT` (no GUID type, no UUID default); harness `SqliteTestHarness.CreateAsync(ddl)`
  uses a uniquely-named **shared in-memory** DB with a keeper connection (single-writer); generated-key
  upsert is `INSERT … ON CONFLICT` (atomic).
- **Phase 1** added `GuidItem` fixtures + `GeneratedKeyUpsertConcurrencyTests` to the SqlServer +
  PostgreSql test projects — the model to mirror. The MySQL fixture is the same shape as Phase 1's.

## 4. Design

### 4.1 MySQL (full parity)
- `tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs`: `[InquiryTable("TGuidItem")]` with
  `[InquiryKey("Id", UseDatabaseDefault = true)] Guid? Id` + `[InquiryColumn] string Name`.
- `tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs`: `UpsertAsync`, `UpsertReturningAsync`,
  `SelectByKeyAsync(Guid? id)`, `SelectAllAsync`.
- `tests/Inquiry.MySql.Tests/GeneratedKeyUpsertConcurrencyTests.cs`, DDL via `CreateFromDdlAsync`:
  `CREATE TABLE TGuidItem (Id CHAR(36) NOT NULL DEFAULT (UUID()) PRIMARY KEY, Name VARCHAR(100) NOT NULL);`
  - `NullKeyLetsDatabaseGenerateTheGuid`: `UpsertReturningAsync(Id = null)` → returned `Id` is non-null and
    not `Guid.Empty`.
  - `ConcurrentUpsertsOfSameExplicitKeyAllSucceed`: 10 parallel `UpsertAsync` with one fixed explicit
    `Guid` → all succeed, `SelectAllAsync` returns exactly one row.
- **Risk:** MySqlConnector's `GuidFormat` for `CHAR(36)`. If the generated UUID doesn't read back as a
  `Guid`, set `GuidFormat=Char36` on the harness connection string (or assert on the round-trip
  accordingly). Verify live and adjust.

### 4.2 SQLite (generated-key, sequential)
- `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedKeyItem.cs`: `[InquiryTable("TGeneratedKeyItem")]` with
  `[InquiryKey("Id", IsGenerated = true)] long? Id` + `[InquiryColumn] string Name`.
- `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedKeyItemStore.cs`: `UpsertAsync`, `UpsertReturningAsync`,
  `SelectByKeyAsync(long? id)`, `SelectAllAsync`.
- `tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertTests.cs`, DDL via `CreateAsync`:
  `CREATE TABLE TGeneratedKeyItem (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);`
  - `NullKeyLetsDatabaseGenerateTheKey`: `UpsertReturningAsync(Id = null)` → returned `Id` is non-null.
  - `ExplicitKeyUpsertInsertsThenUpdates`: `UpsertAsync(Id = 5, Name = "A")` inserts; `UpsertAsync(Id = 5,
    Name = "B")` updates; `SelectAllAsync` returns exactly one row with `Name == "B"`.

### 4.3 Docs
- `docs/site/develop/roadmap.md`: extend the "Recently resolved" upsert bullet to note that SQLite + MySQL
  parity is now **test-proven** (small edit). No crud.md change (the table already lists both as atomic).

## 5. Testing & verification

- **MySQL:** live via Testcontainers (Docker is available locally) — both tests pass, 0 skipped.
- **SQLite:** in-process — both tests pass.
- Adding entities/stores to the MySql/Sqlite test projects only affects those projects' generated stores;
  the shared generator emission for existing tests is unchanged. Build the two affected test projects.

## 6. Success criteria

1. MySQL: the GUID-default generate test + the 10-way concurrency test pass live (0 skipped).
2. SQLite: the generated-key generate test + the insert-then-update test pass in-process.
3. No SQL builder changes; existing non-Docker suites stay green.
4. Roadmap notes that SQLite + MySQL upsert parity is test-proven.
