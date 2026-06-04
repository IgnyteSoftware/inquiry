# Design — Atomic generated-key upsert (SQL Server + PostgreSQL)

- **Date:** 2026-06-04
- **Status:** Approved (scope + sub-decisions confirmed via scoping questions — see §2).
- **Owner:** in-session work, project process (brainstorm → spec → plan → execute).

## 1. Goal

Make SQL Server and PostgreSQL upserts **atomic** so concurrent upserts of the same key can't produce a
spurious duplicate-key error. Today the **generated-key** path on both engines does a *check-then-act*
across separate statements (a race window); the SQL Server **client-key** MERGE also lacks a range lock.
The data is never corrupted (exactly one row survives — already tested), but a racing caller can get a
`duplicate key` exception instead of a clean upsert.

This upgrades the contract from *"one row survives, but a parallel caller may get a duplicate-key error"*
to *"one row survives **and** no caller gets a spurious error."* It applies uniformly to `IsGenerated`
(IDENTITY/SERIAL) and `UseDatabaseDefault` (e.g. `uniqueidentifier DEFAULT NEWSEQUENTIALID()`) keys.

## 2. Scope decisions (from scoping questions)

- **Full atomic fix on both engines** (SQL Server + PostgreSQL generated-key paths).
- **Also harden the SQL Server client-key MERGE** with `HOLDLOCK` (same race class; one-line hint; makes
  all SQL Server upserts race-safe and consistent).
- **GUID/DB-default keys** are first-class: add coverage for a `uniqueidentifier DEFAULT NEWSEQUENTIALID()`
  (PostgreSQL: `DEFAULT gen_random_uuid()`) key — there is **no test for this today**.
- This spec is **Phase 1: PostgreSQL + SQL Server**. The other engines are covered by the phased
  parity plan in §7 — SQLite/MySQL are already atomic (Phase 2 proves it with tests); Oracle generated-key
  upsert is currently unsupported and gets its own later spec (Phase 3). Out of scope *here*: emitting DDL
  default expressions (the column DEFAULT is schema-owned; Inquiry only omits the key from the INSERT so
  the default fires).

## 3. Current state

- **`DatabaseMaySupplyKey(context)`** = `key.IsGenerated || key.UseDatabaseDefault` (`SqlBuilder.cs:452`).
  Both kinds take the generated-key upsert path; the key is **omitted from the INSERT** (`SqlBuildContext`
  `InsertableColumns` excludes `IsGenerated`/`UseDatabaseDefault`), so a column `DEFAULT` (e.g.
  `NEWSEQUENTIALID()`) fires. The null→generate branch only emits when the key is **nullable**
  (`StoreOperationEmitter.cs:1189`).
- **SQL Server generated-key** (`SqlServerSqlBuilder.BuildGeneratedKeyUpsertSql`, ~153-177):
  `IF @key IS NULL <insert generated> ELSE IF EXISTS(… key=@key) <update> ELSE <insert explicit>`. The
  `EXISTS … ELSE INSERT` pair is the race. Client-key path (~84-104) uses `MERGE … WHEN MATCHED/NOT
  MATCHED` **without** `HOLDLOCK`.
- **PostgreSQL generated-key** (`PostgreSqlSqlBuilder.BuildGeneratedKeyUpsertSql`, ~91-126): non-returning
  is `UPDATE … ; INSERT … WHERE @key IS NULL ; INSERT explicit … WHERE NOT EXISTS(…)`; returning is a
  `WITH updated / inserted_generated / inserted_explicit` CTE. The explicit `NOT EXISTS … INSERT` is the
  race. Client-key path uses `INSERT … ON CONFLICT` (already atomic).
- **Tests:** `tests/Inquiry.{Provider}.Tests/UpsertConcurrencyTests.cs` cover the **client-key** path only;
  the SQL Server/Oracle ones *tolerate* a duplicate-key exception on a racing call (documented limitation).
  No generated-key concurrency test; no `NEWSEQUENTIALID`/`gen_random_uuid()` key test.

## 4. Design

### 4.1 SQL Server (`SqlServerSqlBuilder`)
- **Client-key `BuildUpsertSql` / `BuildUpsertReturningSql`:** add `WITH (HOLDLOCK)` to the MERGE target:
  `MERGE INTO <table> WITH (HOLDLOCK) AS target USING (…) …`. (HOLDLOCK = a serializable range lock that
  reserves the key, so a concurrent same-key MERGE waits instead of colliding.)
- **Generated-key `BuildGeneratedKeyUpsertSql`:** keep the `IF @key IS NULL` generated-insert branch
  (plain INSERT; inherently race-free — a fresh IDENTITY/`NEWSEQUENTIALID()` per row). Replace the
  `ELSE IF EXISTS … UPDATE … ELSE INSERT` with the **atomic MERGE + HOLDLOCK** on the explicit key
  (reusing `BuildSourceSelect`/`BuildSourceJoin`), with `OUTPUT INSERTED.*` for the returning variant:
  ```sql
  IF @Id IS NULL
  BEGIN <insert omitting key [OUTPUT INSERTED.*]> END
  ELSE
  BEGIN
    MERGE INTO t WITH (HOLDLOCK) AS target USING (SELECT @Id AS k0) AS source ON target."Id" = source.k0
    WHEN MATCHED THEN UPDATE SET …
    WHEN NOT MATCHED THEN INSERT ("Id", cols) VALUES (@Id, params)
    [OUTPUT INSERTED.*];
  END
  ```

### 4.2 PostgreSQL (`PostgreSqlSqlBuilder`)
- **Client-key path:** unchanged (already `INSERT … ON CONFLICT … DO UPDATE`, atomic).
- **Generated-key `BuildGeneratedKeyUpsertSql`:** keep the null→generate INSERT (race-free). Replace the
  explicit arm's `UPDATE` + `INSERT … WHERE NOT EXISTS` with a single atomic
  `INSERT (key, cols) … ON CONFLICT (key) DO UPDATE` (the explicit key is provided, so no sequence value
  is consumed — this was the original reason `ON CONFLICT` was avoided, but it only applies when the key
  is omitted):
  - **Non-returning** (two statements; exactly one inserts a row):
    ```sql
    INSERT INTO t (cols) SELECT params WHERE @Id IS NULL;
    INSERT INTO t ("Id", cols) SELECT @Id, params WHERE @Id IS NOT NULL
      ON CONFLICT ("Id") DO UPDATE SET …;
    ```
  - **Returning** (single statement, one result set — data-modifying CTEs):
    ```sql
    WITH ins_gen AS (
      INSERT INTO t (cols) SELECT params WHERE @Id IS NULL RETURNING selectCols),
    ins_upsert AS (
      INSERT INTO t ("Id", cols) SELECT @Id, params WHERE @Id IS NOT NULL
      ON CONFLICT ("Id") DO UPDATE SET … RETURNING selectCols)
    SELECT selectCols FROM ins_gen UNION ALL SELECT selectCols FROM ins_upsert;
    ```

### 4.3 Tests
- **Emission tests** (`tests/Inquiry.Generators.Tests`): update the SQL Server upsert (client + generated)
  and PostgreSQL generated-key-upsert expected SQL to the new forms. Non-SQL-Server/non-PostgreSQL
  emission stays byte-identical (regression-assert this).
- **Live generated-key concurrency tests** (SQL Server + PostgreSQL): N parallel upserts of the **same
  explicit key** for a not-yet-existing row → **all succeed**, exactly one row, no exception. Add to (or
  alongside) `UpsertConcurrencyTests`.
- **Tighten the SQL Server client-key concurrency test:** with `HOLDLOCK` it should now expect **all**
  parallel upserts to succeed (drop the "tolerate a duplicate-key exception" allowance).
- **GUID / DB-default-key coverage** (SQL Server + PostgreSQL): a small fixture entity with a nullable
  `Guid?` `UseDatabaseDefault` key, its table created with `DEFAULT NEWSEQUENTIALID()` (SQL Server) /
  `DEFAULT gen_random_uuid()` (PostgreSQL), scoped to those two test projects. Tests: generate path
  (`null` → DB supplies the GUID, returned via OUTPUT/RETURNING), explicit-key upsert, and concurrency.

### 4.4 Docs
- `docs/site/articles/features/crud.md` — flip the SQL Server + PostgreSQL **generated-key** cells (and the
  SQL Server **client-key** cell) in the "Upsert concurrency semantics" table to atomic; drop the
  "racing call may get a duplicate-key error" caveat for SQL Server.
- `docs/site/develop/roadmap.md` — move "harden generated-key upsert atomicity" to *Recently resolved*.

## 5. Testing & verification

- Docker is available in this environment, so the live generated-key concurrency + GUID tests run locally
  on SQL Server and PostgreSQL (Testcontainers) — both paths verified end-to-end, not just by emission.
- TDD: update the emission test to the new SQL (red) → change the builder (green); add the live tests and
  run them against the real engines.
- Non-Docker suites (generator + SQLite) stay green; SQLite/MySQL/Oracle emission unchanged.

## 6. Success criteria

1. SQL Server client-key + generated-key upserts use `MERGE … WITH (HOLDLOCK)`; PostgreSQL generated-key
   explicit arm uses `INSERT … ON CONFLICT`. Returning variants preserved (one result set).
2. Generated-key concurrency tests (SQL Server + PostgreSQL): N parallel same-explicit-key upserts all
   succeed, one row, no exception.
3. SQL Server client-key concurrency test tightened to expect all-succeed.
4. A `uniqueidentifier DEFAULT NEWSEQUENTIALID()` / `gen_random_uuid()` key is tested: generate path,
   explicit upsert, and concurrency — all green.
5. SQLite/MySQL/Oracle emission byte-identical; all non-Docker suites green.
6. `crud.md` upsert-concurrency table and the Roadmap reflect the new guarantees.

## 7. Path to full-engine parity (phased)

The north star is: **all five engines support generated-key upsert with the same semantics.**

| Engine | Generated-key upsert today | Plan |
|---|---|---|
| SQLite | ✅ supported, atomic (`INSERT … ON CONFLICT`) | no SQL change; Phase 2 adds matching concurrency/GUID tests for parity |
| MySQL | ✅ supported, atomic (`INSERT … ON DUPLICATE KEY UPDATE` + `LAST_INSERT_ID` echo) | no SQL change; Phase 2 adds matching tests |
| PostgreSQL | ✅ supported, **not atomic** (check-then-act) | **Phase 1 (this spec)** |
| SQL Server | ✅ supported, **not atomic** (check-then-act; client-key MERGE lacks HOLDLOCK) | **Phase 1 (this spec)** |
| Oracle | ❌ **unsupported** (`INQ039` — a MERGE can't match a NULL generated key) | **Phase 3 (own spec)** |

- **Phase 1 (now):** PostgreSQL + SQL Server (this spec).
- **Phase 2 (after Phase 1 confirmed green):** verify SQLite + MySQL are atomic and add the same
  generated-key concurrency + GUID-default-key tests, so parity is *proven*, not assumed. Tests only;
  likely no SQL change.
- **Phase 3 (separate spec):** implement Oracle generated-key upsert, removing the `INQ039` stub.

**Oracle feasibility (Phase 3 preview).** Yes, the approach applies — Oracle already emits anonymous
PL/SQL blocks with a `:rc` ref cursor for RETURNING and already does generated-key insert-returning
(`RETURNING key INTO v_key`). A generated-key upsert maps to a PL/SQL block:
`BEGIN IF :Id IS NULL THEN <insert; IDENTITY/sequence generates> ELSE MERGE INTO t USING (SELECT :Id FROM
dual) ON (…) WHEN MATCHED UPDATE WHEN NOT MATCHED INSERT; END IF; END;` (wrapped with `OPEN :rc FOR
SELECT …` for returning). Two caveats: (a) notably more involved (PL/SQL + ref cursor + binding); (b)
Oracle has **no `HOLDLOCK` equivalent** for MERGE, so a concurrent first-insert of the *same explicit*
key can still raise `ORA-00001` (matching Oracle's existing client-key MERGE; the "one row survives"
guarantee holds; full race-elimination would need `SERIALIZABLE` or app-level locking). The common
DB-generates-the-key (`null`) pattern has no race. Hence its own spec.
