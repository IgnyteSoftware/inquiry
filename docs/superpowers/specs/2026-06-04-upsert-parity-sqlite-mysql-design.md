# Design — Generated-key upsert parity (Phase 2: SQLite + MySQL)

- **Date:** 2026-06-04
- **Status:** Approved. **Scope expanded** via scoping questions (2026-06-04): originally tests-only, it now
  includes a **MySQL builder + connection-factory change** so MySQL supports a database-generated GUID key
  (`UpsertReturningAsync(Id = null)` returns a DB-generated GUID), matching SQL Server's Phase 1 contract.
- **Owner:** in-session work, project process (brainstorm → spec → plan → execute).

## 1. Goal

Phase 2 of the all-engine upsert-parity effort (see
[`2026-06-04-generated-key-upsert-atomicity-design.md`](2026-06-04-generated-key-upsert-atomicity-design.md)
§7). Bring SQLite + MySQL generated-key upsert to demonstrated parity with the Phase 1 engines:

1. **SQLite** — already atomic and already covered by `GeneratedKeyUpsertIntegrationTests`; close the one
   genuine gap (a single-row guarantee after insert-then-update). Tests only.
2. **MySQL (integer generated key)** — already works (`AUTO_INCREMENT` via `LAST_INSERT_ID()` echo); prove
   it with the same generate + concurrency tests the other engines have. Tests only.
3. **MySQL (GUID generated key)** — **does not work today** and is the substantive part of this phase. Make
   a `Guid?` `UseDatabaseDefault` key behave like SQL Server's `uniqueidentifier DEFAULT NEWSEQUENTIALID()`:
   `UpsertReturningAsync(Id = null)` returns a database-generated GUID. Builder + connection-factory change.

## 2. Scope decisions (from scoping questions)

- **SQLite:** *Add only the missing delta.* Reuse the existing `GeneratedItem` fixture; add a `SelectAllAsync`
  to its store and **one** test asserting an explicit-key insert-then-update leaves exactly one row. No new
  fixture (the spec's original `GeneratedKeyItem` would duplicate `GeneratedItem`).
- **MySQL:** *Support both* integer and GUID generated keys.
  - Integer `AUTO_INCREMENT` generated key: tests only (the path already works).
  - GUID generated key: *make it work* — a real **builder change** (beyond tests-only).
- **GUID generation mechanism:** *Server-side `UUID()` via a user variable* (faithful to "DB-generated"),
  **not** client-side `Guid.NewGuid()`. Requires Inquiry to enable `AllowUserVariables=true` on MySQL
  connections.
- **Oracle** remains Phase 3 (unchanged).

## 3. Current state

- **SQLite:** `GeneratedItem` (`[InquiryKey("Id", IsGenerated = true)] int? Id` + `Name`),
  `GeneratedItemStore` (`SelectByKeyAsync`, `UpsertReturningAsync`), and `GeneratedKeyUpsertIntegrationTests`
  already prove: null key generates, explicit key inserts, explicit key updates. `Schemas.GeneratedItem` DDL
  is `Id INTEGER PRIMARY KEY` (rowid alias — auto-assigns; no `AUTOINCREMENT` needed). Harness
  `SqliteTestHarness.CreateAsync(ddl, namePrefix)` (shared in-memory, single keeper connection).
- **MySQL:** no generated-key upsert test exists (`UpsertConcurrencyTests` covers the *client-key* path via
  `Customer`). Harness `MySqlTestHarness.CreateFromDdlAsync(adminConn, ddl, namePrefix)`; container
  `mysql:8.4`. The generated-key upsert path (`MySqlSqlBuilder.BuildGeneratedKeyUpsertSql`) **binds the key
  into the INSERT** (`(Id, cols) VALUES (@Id, …)`) and emulates RETURNING by reading the row back via
  `LAST_INSERT_ID()` — which only tracks `AUTO_INCREMENT`. Consequences for a `CHAR(36) DEFAULT (UUID())`
  key: `@Id = NULL` becomes an explicit NULL into a `NOT NULL` column → **error** (a plain `DEFAULT` does not
  fire on explicit NULL; only `AUTO_INCREMENT` treats NULL as "generate"), and `LAST_INSERT_ID()` cannot
  return a GUID anyway. So **DB-generated GUID keys are unsupported on MySQL today.**
- **MySQL connection factory** (`MySqlInquiryConnectionFactory`) uses the connection string verbatim;
  `AllowUserVariables` defaults to **false** in MySqlConnector.
- **Builder API:** `DatabaseMaySupplyKey(context)` = single key && (`IsGenerated` || `UseDatabaseDefault`).
  `context.KeyColumns[0]` exposes `.TypeClass` (`DbTypeClass.Guid`), `.IsGenerated`, `.UseDatabaseDefault`;
  `context.QuotedKeyColumns[0]`, `context.KeyParameters[0]`, `context.InsertColumns`,
  `context.InsertParameters`, `context.SelectColumns`. `InsertColumns` excludes the key for
  `DatabaseMaySupplyKey`; the generated-key path re-prepends it.

## 4. Design

### 4.1 SQLite (delta only) — tests only

- `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedItemStore.cs`: add
  `[InquirySelectAll] public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken ct = default);`
- `tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertIntegrationTests.cs`: add one test
  `ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow` — `UpsertReturningAsync(Id = 5, Name = "A")` then
  `UpsertReturningAsync(Id = 5, Name = "B")`, then `SelectAllAsync()` → `Assert.Single`, `Name == "B"`.

### 4.2 MySQL integer generated key — tests only

- `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItem.cs`:
  `[InquiryTable("TGeneratedItem")]` with `[InquiryKey("Id", IsGenerated = true)] long? Id` +
  `[InquiryColumn] string Name`.
- `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItemStore.cs`: `UpsertAsync`, `UpsertReturningAsync`,
  `SelectByKeyAsync(long? id)`, `SelectAllAsync`.
- `tests/Inquiry.MySql.Tests/GeneratedKeyUpsertTests.cs` (DDL via `CreateFromDdlAsync`):
  `CREATE TABLE TGeneratedItem (Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, Name VARCHAR(100) NOT NULL);`
  - `NullKeyLetsDatabaseGenerateTheKey`: `UpsertReturningAsync(Id = null)` → returned `Id` non-null and `> 0`.
  - `ConcurrentUpsertsOfSameExplicitKeyAllSucceed`: 10 parallel `UpsertAsync(Id = 5, Name = "Co_" + i)` →
    `SelectAllAsync` returns exactly one row whose `Name` is one of the inputs.

### 4.3 MySQL GUID generated key — builder + connection change (the substantive part)

#### 4.3.1 `MySqlSqlBuilder` — new GUID-key branch

A helper distinguishes the GUID case from the integer (`AUTO_INCREMENT`) case:

```csharp
private static bool DatabaseSuppliesGuidKey(SqlBuildContext context)
    => DatabaseMaySupplyKey(context) && context.KeyColumns[0].TypeClass == DbTypeClass.Guid;
```

`BuildUpsertSql` / `BuildUpsertReturningSql` check `DatabaseSuppliesGuidKey` **before** the existing
`DatabaseMaySupplyKey` (integer) branch; integer and client-key paths are unchanged.

- **Non-returning** (no user variable needed — nothing is read back):
  ```sql
  INSERT INTO `t` (`Id`, cols) VALUES (COALESCE(@Id, UUID()), params)
    ON DUPLICATE KEY UPDATE <OnDuplicateKeyAssignments>
  ```
  `COALESCE(@Id, UUID())`: explicit key passes through; null key → server generates a `UUID()`.

- **Returning** (capture the generated/explicit key in a user variable, then select by it):
  ```sql
  SET @_inquiry_genkey = COALESCE(@Id, UUID());
  INSERT INTO `t` (`Id`, cols) VALUES (@_inquiry_genkey, params)
    ON DUPLICATE KEY UPDATE <OnDuplicateKeyAssignments>;
  SELECT selectCols FROM `t` WHERE `Id` = @_inquiry_genkey
  ```
  The trailing `SELECT` is the only row-returning result set, so the existing pipeline
  (`CommandBehavior.SingleResult`) consumes it unchanged — the leading `SET` and `INSERT` produce no result
  set. Works for both branches: insert → new row keyed by the variable; update → existing row already keyed
  by the (explicit) variable value.

The key is re-prepended to the insert list with `JoinSql(keyColumn, context.InsertColumns)` exactly as the
existing `BuildGeneratedKeyUpsertSql` does. `OnDuplicateKeyAssignments` is reused as-is (binds entity values
for `UseDatabaseDefault` non-key columns; `VALUES(col)` for the rest).

#### 4.3.2 `MySqlInquiryConnectionFactory` — enable user variables

The returning path uses a `@_inquiry_genkey` **user variable**, which MySqlConnector only honors when
`AllowUserVariables=true` (otherwise every `@name` is treated as a command parameter and the unmatched
variable throws). The factory normalizes the connection string in its constructor:

```csharp
_connectionString = new MySqlConnectionStringBuilder(connectionString)
{
    AllowUserVariables = true,
}.ConnectionString;
```

Safe: all Inquiry SQL is compile-time-constant text with bound parameters; the only unbound `@name` Inquiry
ever emits is the intentional `@_inquiry_genkey`. The behavioral change is narrow (an unmatched `@name` is now
a user variable rather than an error), and Inquiry never emits one unintentionally.

#### 4.3.3 Emission tests + unchanged-path regression

- `tests/Inquiry.Generators.Tests`: add MySQL emission assertions for the GUID generated-key upsert
  (non-returning + returning) to the new SQL forms above.
- Assert the MySQL **integer** generated-key upsert and **client-key** upsert emission are **byte-identical**
  to today (regression guard), and that SQLite/SqlServer/PostgreSql/Oracle emission is unchanged.

#### 4.3.4 Live tests (GUID fixture)

- `tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs`:
  `[InquiryTable("TGuidItem")]` with `[InquiryKey("Id", UseDatabaseDefault = true)] Guid? Id` +
  `[InquiryColumn] string Name`.
- `tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs`: `UpsertAsync`, `UpsertReturningAsync`,
  `SelectByKeyAsync(Guid? id)`, `SelectAllAsync`.
- `tests/Inquiry.MySql.Tests/GuidKeyUpsertTests.cs` (DDL via `CreateFromDdlAsync`):
  `CREATE TABLE TGuidItem (Id CHAR(36) NOT NULL DEFAULT (UUID()) PRIMARY KEY, Name VARCHAR(100) NOT NULL);`
  (the `DEFAULT (UUID())` documents intent; Inquiry now supplies the `UUID()` itself, so the column default
  is not what fires.)
  - `NullKeyLetsDatabaseGenerateTheGuid`: `UpsertReturningAsync(Id = null)` → returned `Id` non-null and not
    `Guid.Empty`.
  - `ConcurrentUpsertsOfSameExplicitKeyAllSucceed`: 10 parallel `UpsertAsync(Id = key, …)` for one fixed
    `Guid` → `SelectAllAsync` returns exactly one row.

### 4.4 Docs

- `docs/site/develop/roadmap.md`: under *Recently resolved*, note SQLite + MySQL generated-key upsert parity
  is test-proven, and that MySQL now supports **DB-generated GUID keys** (server-side `UUID()`); record the
  `AllowUserVariables=true` behavior as a deliberate provider default.
- `docs/site/articles/features/crud.md`: confirm the "Upsert concurrency semantics" table reflects MySQL
  generated-key (integer + GUID) as supported/atomic; add a short note that on MySQL a DB-generated GUID key
  is realized via server-side `UUID()` (Inquiry enables `AllowUserVariables`).

## 5. Testing & verification

- **SQLite:** in-process — the new single-row test passes; existing SQLite suite stays green.
- **MySQL:** live via Testcontainers (`mysql:8.4`, Docker available locally) — integer + GUID tests pass,
  0 skipped. **GuidFormat check:** MySqlConnector's default reads `CHAR(36)` as `Guid` (Char36 when
  `OldGuids=false`); if the round-trip fails, also set `GuidFormat=Char36` in the factory builder. Verify
  live and adjust.
- **Emission:** `Inquiry.Generators.Tests` green; non-MySQL dialects byte-identical; MySQL integer/client
  paths byte-identical.
- TDD: write/adjust the emission test to the new MySQL GUID SQL (red) → change the builder (green); add the
  live tests and run them against the real engine.

## 6. Success criteria

1. SQLite: insert-then-update leaves exactly one row (new test green); existing SQLite tests still green.
2. MySQL integer generated key: null→generate→returned and 10-way explicit-key concurrency→one row, live.
3. MySQL GUID generated key: `UpsertReturningAsync(Id = null)` returns a non-empty DB-generated GUID;
   10-way explicit-GUID concurrency→one row, live.
4. `MySqlInquiryConnectionFactory` enables `AllowUserVariables=true`.
5. Emission: new MySQL GUID upsert SQL asserted; MySQL integer/client + all other dialects byte-identical.
6. Roadmap + crud.md reflect MySQL GUID generated-key support and the `AllowUserVariables` default.

## 7. Risks

- **`CommandBehavior.SingleResult` + leading `SET`/`INSERT`.** The trailing `SELECT` must be the first
  row-returning result set. The existing `INSERT; SELECT` emulation already relies on this; adding a
  non-result `SET` should not change it. Confirmed by the live returning test.
- **`GuidFormat` round-trip** for `CHAR(36)` (see §5) — low risk; resolution is one factory setting.
- **`AllowUserVariables=true` as a forced default.** Narrow, safe behavior change (see §4.3.2); documented.
- **`VALUES(col)` deprecation** (MySQL 8.0.20+) — unchanged from today; retained for MariaDB/5.7 compatibility.
