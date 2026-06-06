# W4 — Automatic Prepared-Statement Reuse

> See [README.md](README.md). Depends on: **F4** (command hook), **F6** (DbType metadata — prerequisite, valuable standalone). Size: **M**. Contention: **MEDIUM–HIGH** (`StoreOperationEmitter` binder + pipeline).

## 1. Feature summary & surface
inquiry bakes every statement as `const string` — ideal for `Prepare()`. Make the pipeline call `PrepareAsync()` (and ensure parameter-type metadata exists so preparation is real), gated by a DI option. Current pre-release behavior is **default Auto**, with `PreparedStatementMode.None` as the opt-out.
```csharp
services.AddInquiry(o => o.PrepareStatements = PreparedStatementMode.None); // Auto (default) | None
```
The default changed before release after the PostgreSQL benchmark showed the `Auto` path faster for stable Npgsql SQL. Capability gating keeps providers with connection-scoped prepared state on a silent no-op path.

## 2. Approach (recommended C — hybrid, capability-gated)
- **A** explicit `PrepareAsync()` every command — but only pays where prepared state survives connection dispose (Npgsql pool-level cache: yes; SqlClient: no, handle dies on dispose; SQLite: per-op-new-connection negates).
- **B** lean entirely on provider auto-prepare — zero code/risk but doesn't deliver the feature or help SQLite.
- **C (chosen):** `PreparedStatementMode` option (`Auto` default, `None` opt-out) + `SupportsPersistentPreparedStatements` capability gate (Npgsql true; SqlClient false → rely on its plan cache; SQLite configurable, default false) + **thread compile-time `DbType` metadata into binders so `Prepare()` is effective**. The DbType work (F6) is worth doing regardless.

## 3. Design
### 3a. The prerequisite (F6 — the real work)
Generated binders today set only `ParameterName` + `Value`, no `DbType`/`Size`. `Prepare()` needs fixed parameter types before the call, or providers re-infer per call (thrashing the prepared statement). Add `Inquiry.Generators.Shared/Infrastructure/DbTypeMapper.cs` (pure `SpecialType/IsGuid → System.Data.DbType` constant; symbol-free, cache-friendly). Emit `_p{i}.DbType = <mapped>;` in `AppendBinderLambda`/`AppendPositionalParameters`/`EmitStoredProcedure`. For fixed-width types that's enough; for variable-length (`string`/`byte[]`) emit `DbType` only and gate `Prepare()` to providers tolerant of unsized params (Npgsql is) — leave SqlClient (which wants `Size`) to its plan cache.
### 3b. Pipeline
Inject options + capability; between bind and execute in each (~8) execution method: `if (_prepare) await dbCommand.PrepareAsync(ct);` where `_prepare = options.Auto && factory.SupportsPersistentPreparedStatements && command.CommandType != StoredProcedure`. Mirror in `TransactedInquiryRequestPipeline` (more valuable — same connection across the tx). Single guarded statement preserves the no-interceptor fast path when off.
### 3c. Per-provider
| Provider | reuse model | plan |
|---|---|---|
| Npgsql | server-side, per pooled physical connection, survives dispose | capability `true`; document `Max Auto Prepare` |
| Microsoft.Data.SqlClient | handle scoped to open connection, lost on dispose | `false`; rely on SQL Server plan cache |
| Microsoft.Data.Sqlite | in-process, tied to connection | `false` default; opt-in for long-lived/pooled |

### 3d. Connection lifecycle
inquiry opens/disposes per op → only Npgsql's pool-level cache + the transacted pipeline see real reuse. This is why the capability gate exists + default off. This workstream does NOT introduce connection retention (separate, larger, thread-safety-laden change).

## 4. Implementation steps (TDD)
1. **F6 DbType in binders.** `DbTypeMapper` + unit test (mapping); update 3 emit sites. *Verify:* snapshot tests re-emit with `_pN.DbType`; full generator suite (incremental cache stable).
2. `InquiryOptions` + `PreparedStatementMode` + `AddInquiry(Action<InquiryOptions>)` overload. *Verify:* DI default `Auto`.
3. `SupportsPersistentPreparedStatements` on `IInquiryConnectionFactory` (default-interface-member `=> false`; Npgsql `true`). *Verify:* per-provider flag test.
4. Wire `PrepareAsync()` into both pipelines (exclude StoredProcedure). *Verify:* fake-`DbCommand` asserts Prepare called/not per mode×capability×CommandType; integration (SQLite + Npgsql) Auto == None results; prepared query correct after param value changes.
5. Benchmark (Npgsql, `[Params(None,Auto)]`, pooled, `Max Auto Prepare` off) over SelectByKey + a 2–3-join eager select. *Verify:* mean-time reduction on Auto, no alloc regression when None.
6. Docs.

## 5. Shared-file contention map
- **MODIFY (high):** `StoreOperationEmitter.cs` (AppendBinderLambda/AppendPositionalParameters/EmitStoredProcedure — hottest binding file), `Pipeline/InquiryRequestPipeline.cs` + `TransactedInquiryRequestPipeline.cs` (~8 methods + ctor), `Connections/IInquiryConnectionFactory.cs` (capability), `DependencyInjection/InquiryServiceCollectionExtensions.cs` (overload + options), 3 connection factories.
- **ADD:** `Infrastructure/DbTypeMapper.cs`, `InquiryOptions.cs` + `PreparedStatementMode.cs`, tests + benchmark.
- **New abstraction:** the capability flag — consider a dedicated `IInquiryProviderCapabilities` if W3 also needs capability negotiation (avoid repeated factory edits).

## 6. Cross-workstream dependencies
- **W1 `IN` collision (critical):** variadic `IN` expansion makes SQL non-const → invalidates prepared statements per cardinality. Coordinate: exclude IN from prepare OR use array params (Npgsql `= ANY(@ids)`) keeping SQL const + prepareable. Flag explicitly.
- **W3 batch/bulk:** batch chunk-template const is prepareable; share `InquiryOptions` + capability interface; prepare the `DbBatch`. Land W4's pipeline hook first.
- **Sequencing:** F6 (DbType) **first, standalone** (prerequisite + benefits W3); then options/capability; then pipeline wiring. Shares F4 command hook with E2/E3.

## 7. Test strategy
Generator: snapshot `DbType` emitted, mapping unit tests, incremental-cache stability. Runtime: fake-`DbCommand` Prepare called/not per (mode × capability × CommandType); stored-proc never prepared. Integration: full CRUD on SQLite + Npgsql with Auto == None; correctness after param-value change. Perf: BenchmarkDotNet Npgsql, measurable Auto improvement, no alloc regression when None.

## 8. Risks / open questions
- Connection lifecycle is the crux — capability gate mitigates net-negative on SqlClient/SQLite.
- Variable-length types without stable `Size` thrash some providers → emit DbType only, rely on Npgsql's no-size-needed; gate SqlClient out.
- Dedicated `IInquiryProviderCapabilities` vs extending `IInquiryConnectionFactory`? Lean dedicated (W3 also needs flags). DIMs fine (net6.0+).

## 9. Size: **M** — pipeline wiring + options + capability are small/mechanical; the F6 DbType metadata flow (hottest emit file + new mapper + snapshot churn) is the substantive bounded part. Cross-provider benchmark (real Postgres in CI) is the main schedule risk.
