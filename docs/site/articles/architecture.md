# Architecture deep-dive

This page is the long-form complement to [How it works](concepts.md). It documents the *implementation* — how the generator framework is laid out, how the request pipeline is structured, and the design constraints that shaped both.

For the conceptual pipeline with diagrams, start at [How it works](concepts.md); this page goes deeper into the generator framework, SQL building, and the runtime. For project status, the supported-dialect matrix, and the roadmap, see [Project status](../develop/project-status.md) and the [Roadmap](../develop/roadmap.md).

## Quick reference

| Concern | Where it lives |
|---|---|
| Public runtime API (`IInquiry`, attributes, commands, transactions, options) | `src/Inquiry/` |
| Per-dialect Roslyn generator | `src/Inquiry.<Dialect>.Analyzer/` |
| Shared generator framework | `src/Inquiry.Generators.Shared/` |
| Per-dialect runtime provider package | `src/Inquiry.<Dialect>/` |
| SQL builder per dialect | `Inquiry.Generators.Shared/SqlBuilder` + dialect-specific subclasses |
| Materializer emission | `Inquiry.Generators.Shared/MaterializerEmitter` |
| Store-method emission | `Inquiry.Generators.Shared/StoreOperationEmitter` |
| Request pipeline (default) | `src/Inquiry/Pipeline/InquiryRequestPipeline.cs` |
| Request pipeline (transacted) | `src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs` |
| Transaction handle abstractions | `src/Inquiry/Transactions/IInquiryTransaction.cs`, `InquiryTransactionBase.cs` |
| Top-level (real) transaction | `src/Inquiry/Transactions/InquiryTransaction.cs` |
| Savepoint (nested) transaction | `src/Inquiry/Transactions/SavepointInquiryTransaction.cs` |
| Generated DDL emission | `Inquiry.Generators.Shared/SchemaEmitter.cs` |
| DI registration emission | `Inquiry.Generators.Shared/RegistrationEmitter.cs` |

## Key design constraints

1. **Compile-time SQL is non-negotiable.** Every SQL statement is a `const string`. The runtime never builds, formats, or interpolates SQL.
2. **One dialect per assembly.** `[InquiryDialect]` is `AllowMultiple = false`. Multi-dialect = multi-assembly.
3. **The runtime ships zero SQL.** `src/Inquiry/` has no `SELECT`, no `INSERT`, nothing. All SQL lives in the generated partials.
4. **Materializers are struct-specialized.** Generated stores call the struct-materializer overloads on the pipeline; the JIT emits a separate body per concrete struct so the per-row `materializer.Materialize(reader)` call inlines (no interface dispatch).
5. **Read streaming.** Generated stores pass `CommandBehavior.SequentialAccess`. Generated materializers read every column exactly once in ascending ordinal order, so this is safe and roughly halves allocation on large/wide reads.
6. **Diagnostics at compile time.** Any condition the generator can detect (unknown column, missing key, unsupported return shape, conflicting attributes) produces an `INQxxx` diagnostic at the source location.

## SQL building

All SQL is produced at compile time by an internal `SqlBuilder` hierarchy in `Inquiry.Generators.Shared`. The runtime ships **zero SQL** — no abstract dialect, no per-call build, no statement cache. Each generated store carries the SQL it needs as `private const string` fields.

```csharp
// src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs
public abstract class SqlBuilder
{
    public abstract string DialectName { get; }
    public abstract string QuoteIdentifier(string identifier);
    public virtual  string ParameterName(string logical);   // default: "@" + logical
    public          string QuoteTable(string? schema, string table);

    public abstract string BuildSelectAllSql       (SqlBuildContext ctx);
    public abstract string BuildSelectByKeySql     (SqlBuildContext ctx);
    public abstract string BuildSelectByFieldSql   (SqlBuildContext ctx, IReadOnlyList<IColumn> filterColumns);
    public abstract string BuildInsertSql          (SqlBuildContext ctx);
    public abstract string BuildInsertReturningSql (SqlBuildContext ctx);
    public abstract string BuildUpdateSql          (SqlBuildContext ctx);
    public abstract string BuildUpdateReturningSql (SqlBuildContext ctx);
    public abstract string BuildDeleteByKeySql     (SqlBuildContext ctx);
    public abstract string BuildUpsertSql          (SqlBuildContext ctx);
    public abstract string BuildUpsertReturningSql (SqlBuildContext ctx);
}
```

`StoreProcessor` builds a `SqlBuildContext` once per (entity, builder) pair — precomputing the quoted table, the select/insert column lists and matching parameters, the SET clauses, and the key WHERE clause — then calls whichever `Build…Sql` methods the store actually needs. Feature capabilities (predicates, pagination, batch, soft-delete, concurrency, …) are added as `virtual`-with-base-default members where the SQL is dialect-uniform, so a new provider inherits them and overrides only what genuinely differs.

| Dialect builder | Identifier quoting | Upsert strategy |
|---|---|---|
| `SqliteSqlBuilder` | `"name"` (double quotes) | `INSERT … ON CONFLICT DO UPDATE` |
| `SqlServerSqlBuilder` | `[name]` (brackets) | `MERGE` (existence branch for generated keys) |
| `PostgreSqlSqlBuilder` | `"name"` (double quotes) | `INSERT … ON CONFLICT … DO UPDATE` (client key); `UPDATE`/`INSERT` CTE (generated key) |
| `MySqlSqlBuilder` | `` `name` `` (backticks) | `INSERT … ON DUPLICATE KEY UPDATE` |
| `OracleSqlBuilder` | `"name"` (double quotes) | `MERGE` |

To change how a statement is emitted for one database without affecting the others, override the matching `Build…Sql` in that provider's builder.

### Dialect selection

Each provider analyzer hardcodes its own dialect name. When Roslyn loads it (because the consumer referenced the matching provider package), it inspects the compilation for `[assembly: InquiryDialect("…")]` — first on the consuming assembly (an explicit override), then on referenced assemblies (provider runtime DLLs ship the attribute pre-applied). If the resolved name matches, the generator emits; otherwise it stays silent so a coexisting provider can claim the build. No dialect attribute at all → the loaded generator treats it as implicit opt-in to its own dialect. Multiple matching dialects surface as `INQ014`.

## Store attributes

All store attributes live in `Inquiry.Stores`. The method must be a `partial` declaration on a `partial class : InquiryStore<TEntity>`, and the last parameter must be `CancellationToken`. The generator emits the constructor and the method bodies into a second partial of the same class — no derived class, no user-written constructor.

| Attribute | Maps to |
|---|---|
| `[InquirySelectAll]` / `[InquirySelectAllEager]` | `BuildSelectAllSql` (+ per-relation child queries for eager) |
| `[InquirySelectOneByKey]` / `[InquirySelectOneByKeyEager]` | `BuildSelectByKeySql` (+ per-relation child queries for eager) |
| `[InquirySelectAllByField("Field")]` | `BuildSelectByFieldSql` |
| `[InquiryInsert]` / `[InquiryInsert(ReturnEntity = true)]` | `BuildInsertSql` / `BuildInsertReturningSql` |
| `[InquiryUpdate]` / `[InquiryUpdate(ReturnEntity = true)]` | `BuildUpdateSql` / `BuildUpdateReturningSql` |
| `[InquiryUpsert]` / `[InquiryUpsert(ReturnEntity = true)]` | `BuildUpsertSql` / `BuildUpsertReturningSql` |
| `[InquiryDelete]` | `BuildDeleteByKeySql` |
| `[InquiryStoredProcedure("Proc")]` | raw `InquiryCommand` with `CommandType.StoredProcedure` |

Entity-mapping attributes live in `Inquiry.Entities`: `[InquiryTable]`, `[InquiryColumn]`, `[InquiryKey]`, `[InquiryForeignKey]`, `[InquiryRelation]`. Beyond this core surface Inquiry also supports richer WHERE predicates, ORDER BY + pagination, batch operations, projections + aggregations, optimistic concurrency, soft deletes, full-text search, and value-converter columns — see [Features](features/crud.md).

## Transactions

`IInquiry.ExecuteInTransactionAsync` is a public helper over the same primitive: it opens a transaction, awaits the caller's delegate, commits on success, and lets dispose roll back on exceptions. `IInquiry.BeginTransactionAsync` opens a fresh `DbConnection` + `DbTransaction` from the connection factory and returns an `IInquiryTransaction`. Two implementation classes back the interface:

- **`InquiryTransaction`** — the top-level case. Owns the connection and the `DbTransaction`. `Commit` calls `DbTransaction.CommitAsync`, `Rollback` calls `RollbackAsync`, `Dispose` rolls back if neither has fired and then disposes the connection.
- **`SavepointInquiryTransaction`** — the nested case. Holds a reference to the outer `TransactedInquiryRequestPipeline` plus a unique savepoint name. `Commit` calls `DbTransaction.ReleaseAsync(name)`; `Rollback` calls `RollbackAsync(name)`; `Dispose` best-effort rolls back to the savepoint if neither has fired. Oracle's lack of explicit savepoint release is handled by catching `NotSupportedException` in `Commit` — the savepoint will be released implicitly when the outer transaction closes.

Both inherit from **`InquiryTransactionBase`**, which holds the root `IInquiry` privately and implements every forwarding method (`tx.ExecuteAsync`, `tx.QueryAsync<T>`, etc.) once. Each forwarding method calls the concrete's `ThrowIfClosed()` before delegating — that's how use-after-close throws `ObjectDisposedException` instead of silently routing to the non-transactional pipeline.

### Ambient routing — how generated stores join

`DefaultInquiry` holds an `AsyncLocal<AmbientTransactionSlot>` field. The slot is a *holder reference* with a mutable `Pipeline` field — not the pipeline directly. That extra indirection exists because `AsyncLocal` values set inside an async callee don't propagate back to the caller. `BeginTransactionAsync` works around it:

1. **Install the slot synchronously**, before any `await`. Caller's async context now sees the holder.
2. Await the connection open + `BeginTransactionAsync(level, ct)` call.
3. Fill in `slot.Pipeline = new TransactedInquiryRequestPipeline(connection, tx, …)`. The caller's reference to the holder sees the mutation.
4. Return an `InquiryTransaction` whose close path detaches the current async flow, then marks the captured holder closed on the first of Commit / Rollback / Dispose.

Every IInquiry method then routes through the ambient slot. Active slot → transacted pipeline reusing one connection. No slot → default pipeline opening a fresh connection per call. Closed captured slot → `ObjectDisposedException`, which prevents async work that started inside a transaction from silently continuing outside it after close. Generated stores (which hold the same DI-scoped `DefaultInquiry`) participate automatically without per-call wiring.

The nested case in `BeginTransactionAsync` short-circuits: if `_ambientSlot.Value?.Pipeline` is already set, it doesn't open a new physical transaction; it calls `outerPipeline.SaveSavepointAsync(name, ct)` and returns a `SavepointInquiryTransaction`. The slot stays pointing at the outer pipeline — savepoints share its physical connection.

### Concurrency guard

`DbConnection` isn't thread-safe. `TransactedInquiryRequestPipeline` serializes access with an `Interlocked.CompareExchange(ref _inFlight, 1, 0)` guard at the top of every operation. A second op starting while another is in flight throws `InvalidOperationException("Cannot start a new Inquiry operation while another operation is in flight on the same transaction.")` instead of corrupting the connection.

Root commit / rollback and the savepoint primitives (`SaveSavepointAsync` / `ReleaseSavepointAsync` / `RollbackToSavepointAsync`) respect the same guard — they're SQL statements on the connection, just like data ops.

### DI lifetimes

- `IInquiry` (`DefaultInquiry`) — **Scoped**. Each DI scope gets its own ambient slot; transactions don't cross scopes.
- `IInquiryRequestPipeline` (`InquiryRequestPipeline`) — **Scoped**. Used by `DefaultInquiry` as the non-transactional fallback.
- `IInquiryConnectionFactory` — **Singleton**. Owns the connection-string + dialect-specific factory logic.
- Generated stores — **Scoped**. Hold the same scoped `IInquiry` instance the rest of the scope uses.
- Materializers — **Singleton**. Stateless; safe to share.

### What's not modeled

- **`System.Transactions` ambient (`TransactionScope`)** — Inquiry uses ADO.NET `DbTransaction` directly. Cross-process / DTC scenarios are out of scope for a single-engine micro-ORM. If you need to participate in a `TransactionScope`, open it before resolving Inquiry and let the provider's `DbConnection` auto-enlist (per its own ADO.NET behavior — the connection will pick up `Transaction.Current` when opened).
- **Cross-DI-scope transactions** — each scope has its own `DefaultInquiry` with its own `AsyncLocal` field; ambient transactions don't leak between scopes.
- **Cross-thread transactions without `ExecutionContext` flow** — `Task.Run` preserves the context by default, so ambient transactions survive it. `Thread.Start` and manual context-suppression do not.
