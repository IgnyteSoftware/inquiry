# E2 — Oracle Provider

> See [README.md](README.md). Depends on: **F4** (command-init hook). Land **late** — must implement every new `SqlBuilder` abstract member added by W1/W2/W5/W7/W8/W9. Size: **L**. Contention: **MEDIUM–HIGH** (parameter-prefix + pipeline hook + materializer type mapping).

## 1. Feature summary & user-facing surface
Fourth provider targeting Oracle 12c+, packaged like the others. ADO.NET: `Oracle.ManagedDataAccess.Core`.
`services.AddInquiry().AddInquiryOracle(connectionString);` + optional `[assembly: InquiryDialect("Oracle")]`.
The SQL-emission half mirrors PostgreSql/SqlServer ~1:1; the real work is **three shared assumptions Oracle violates**: `@` parameter prefix, result-set-based RETURNING, and `reader.GetXxx` type mapping.

## 2. Design
New files (clone PostgreSql provider): `Inquiry.Oracle.csproj`, `AssemblyInfo.cs` (`InquiryDialect("Oracle")`), `OracleInquiryConnectionFactory.cs`, `OracleInquiryServiceCollectionExtensions.cs`, `Inquiry.Oracle.Analyzer.csproj`, `InquiryOracleGenerator.cs`, `OracleSqlBuilder.cs`. Add all to `Inquiry.slnx`.

`OracleSqlBuilder`:
- `DialectName => "Oracle"`.
- **`ParameterName` → `:name`** (override base `@name`). This flows through `SqlBuildContext` so all emitted SQL text is correct (free). The runtime bind layer is the problem — see below.
- `QuoteIdentifier`: **do not blanket-quote** (default). Oracle folds unquoted identifiers to UPPERCASE; quoting forces exact-case and would mismatch DDL. Use unquoted/uppercase policy; test DDL matches. (Biggest open design question — §8.)
- Selects/update/delete: PG-shaped.
- `BuildInsertSql`: no `DEFAULT VALUES` — handle all-generated insert via `VALUES (DEFAULT)` or explicit key.
- `BuildUpsertSql`: `MERGE INTO t USING (SELECT … FROM dual) src ON (…) WHEN MATCHED … WHEN NOT MATCHED …`. `SetClauses` already excludes keys (Oracle MERGE forbids updating ON-clause columns).
- **Returning path:** Oracle `RETURNING … INTO` binds OUT params, NOT a result set — incompatible with the current reader-based returning path. **v1: ship option B** — `OracleSqlBuilder` emits a diagnostic / declares `ReturnEntity` unsupported; support full reads + non-returning Insert/Update/Upsert. Add result-set-returning later via a pipeline OUT-parameter path.

**Parameter-prefix (`:` vs `@`) — central issue.** Two layers:
1. SQL text (dialect-aware, free) — `ParameterName` override handles it.
2. Runtime bind (hardcoded `@` in `StoreOperationEmitter` `_p.ParameterName = "@…"` / `new InquiryParameter("@…")` at ~5 sites). With `OracleCommand.BindByName = true`, ODP.NET matches by name and tolerates the prefix mismatch. But `BindByName` defaults to false and there's no hook today → **needs F4**. Implement `BindByName=true` in `OracleInquiryConnectionFactory.InitializeCommand`. **Verify in integration tests** whether the `@`-prefixed bound names still match `:`-prefixed SQL with BindByName on. If not, fall to the emitter refactor (thread a dialect prefix into `StoreOperationEmitter`, touching shared hot files + emission tests).

## 3. Per-dialect notes
Unquoted/uppercase identifiers; `:name` params + `BindByName=true`; `GENERATED … AS IDENTITY` (12c+) for generated keys; **no native bool** → `NUMBER(1)` but `reader.GetBoolean` throws (document `bool` unsupported in v1 or schedule a dialect-aware materializer); `Guid`→`RAW(16)`; **NUMBER→decimal coercion** (`GetInt32` etc. may throw unless DDL uses precise `NUMBER(p)`); `TIMESTAMP` over `DATE`; `VARCHAR2`/`CLOB`.

## 4. Implementation steps (TDD)
1. Add ODP.NET to `Directory.Packages.props`.
2. Generator emission tests first (model on SqlServer/PG): unquoted identifiers, `:name`, `MERGE … FROM dual`, no `DEFAULT VALUES`. Wire Oracle generator into the test `generators[]`/references arrays.
3. **Invert** the now-stale `UnknownDialectProducesNoStoreSql…` test (it uses `"Oracle"`) — pick a genuinely unknown dialect for the negative case.
4. Implement `OracleSqlBuilder`. *Verify:* generator tests green.
5. Create runtime project. *Verify:* builds; slnx updated.
6. **F4 command hook** (if not already landed): implement `BindByName=true` in the Oracle factory.
7. Add `OracleDdl` to `NorthwindSchema.cs` (precise `NUMBER(p)`, `VARCHAR2`, `RAW(16)`, IDENTITY, unquoted).
8. Integration harness + tests (`INQUIRY_ORACLE_CONNECTION_STRING`, `gvenzl/oracle-free` container). *Verify:* CRUD passes; resolves the prefix-match + type-mapping questions. Add a probe entity with int/Guid/bool/decimal/DateTime.

## 5. Shared-file contention map
- **MODIFY (shared):** `IInquiryConnectionFactory.cs` (F4 hook), `InquiryRequestPipeline.cs` + `TransactedInquiryRequestPipeline.cs` (invoke hook), `InquiryGeneratorTests.cs` (generator array — same lines as **E1**), `NorthwindSchema.cs` (`OracleDdl`), `Directory.Packages.props`, `Inquiry.slnx`.
- **CONDITIONALLY MODIFY (only if prefix must match):** `StoreOperationEmitter.cs` (5 `@` literals — hot, shared), `StoreProcessor.cs` (thread prefix), emission-test assertions.
- **FLAGGED (deferred, large):** `EntityProcessor.cs` `ReadCallForSpecialType`/`GetBoolean` — dialect-aware materializer is the real fix for no-bool/NUMBER; its own cross-cutting workstream, not folded here.
- **ADD:** everything under `src/Inquiry.Oracle*`, `tests/Inquiry.Oracle.Tests/`.

## 6. Cross-workstream dependencies
- **F4** command hook is shared with **W4** (prepared statements) and **E3** (cloud-compat) — co-design the hook shape; land it once. The optional emitter prefix refactor collides with **E1** and **W4** in `StoreOperationEmitter.cs` — if one of {E2, E1, W4} needs it, do it in a small dedicated PR.
- Must implement every new abstract `SqlBuilder` member → schedule after W1/W2/W5/W7/W8/W9 stabilize (or rely on F3 defaults). Pagination → Oracle `OFFSET … FETCH`; FTS → Oracle `CONTAINS`; JSON → Oracle `JSON`/`IS JSON`; arrays → no native equivalent (flag Oracle-limited).
- Coordinate identity DDL with **W7**.

## 7. Test strategy
Generator emission tests (always-on). Opt-in integration via `OracleFactAttribute` (skips without env var) against Oracle Database Free. Integration tests are where the prefix/type-mapping risks resolve empirically.

## 8. Risks / open questions
1. `BindByName` + prefix matching (resolved only by integration test) — decides whether the emitter refactor is needed.
2. RETURNING INTO has no result set → v1 drops `ReturnEntity` (confirm acceptable).
3. NUMBER→decimal coercion — precise `NUMBER(p)` DDL mitigates; arbitrary real-world `NUMBER` breaks hardcoded reads.
4. No native bool — `bool` unsupported v1 or schedule materializer refactor.
5. Unquoted/uppercase identifier policy — confirm with maintainers; affects DDL/tests/expectations.

## 9. Size: **L** — SQL-emission half is mechanical (M), but three shared-code intrusions (command hook across ~20 sites, conditional emitter prefix refactor, RETURNING-INTO gap) + type-mapping validation push it to L.
