# Architecture deep-dive

This page is the long-form complement to [How it works](concepts.md). It documents the *implementation* — how the generator framework is laid out, how the request pipeline is structured, and the design constraints that shaped both.

For the canonical architecture write-up — including the full project layout, the SqlBuilder hierarchy, and the rationale for compile-time `const string` SQL — see the repository's [`README.md`](https://github.com/JakeOverstreet/inquiry/blob/main/README.md).

For project status, supported dialect matrix, and the workstream roadmap, see [`docs/STATUS.md`](https://github.com/JakeOverstreet/inquiry/blob/main/docs/STATUS.md).

## Quick reference

| Concern | Where it lives |
|---|---|
| Public runtime API (`IInquiry`, attributes, pipeline) | `src/Inquiry/` |
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

## Transactions

`IInquiry.BeginTransactionAsync` opens a fresh `DbConnection` + `DbTransaction` from the connection factory and returns an `IInquiryTransaction`. Two implementation classes back the interface:

- **`InquiryTransaction`** — the top-level case. Owns the connection and the `DbTransaction`. `Commit` calls `DbTransaction.CommitAsync`, `Rollback` calls `RollbackAsync`, `Dispose` rolls back if neither has fired and then disposes the connection.
- **`SavepointInquiryTransaction`** — the nested case. Holds a reference to the outer `TransactedInquiryRequestPipeline` plus a unique savepoint name. `Commit` calls `DbTransaction.ReleaseAsync(name)`; `Rollback` calls `RollbackAsync(name)`; `Dispose` best-effort rolls back to the savepoint if neither has fired. Oracle's lack of explicit savepoint release is handled by catching `NotSupportedException` in `Commit` — the savepoint will be released implicitly when the outer transaction closes.

Both inherit from **`InquiryTransactionBase`**, which holds the root `IInquiry` privately and implements every forwarding method (`tx.ExecuteAsync`, `tx.QueryAsync<T>`, etc.) once. Each forwarding method calls the concrete's `ThrowIfClosed()` before delegating — that's how use-after-close throws `ObjectDisposedException` instead of silently routing to the non-transactional pipeline.

### Ambient routing — how generated stores join

`DefaultInquiry` holds an `AsyncLocal<AmbientTransactionSlot>` field. The slot is a *holder reference* with a mutable `Pipeline` field — not the pipeline directly. That extra indirection exists because `AsyncLocal` values set inside an async callee don't propagate back to the caller. `BeginTransactionAsync` works around it:

1. **Install the slot synchronously**, before any `await`. Caller's async context now sees the holder.
2. Await the connection open + `BeginTransactionAsync(level, ct)` call.
3. Fill in `slot.Pipeline = new TransactedInquiryRequestPipeline(connection, tx, …)`. The caller's reference to the holder sees the mutation.
4. Return an `InquiryTransaction` whose `onClose` callback nulls out `slot.Pipeline` on the first of Commit / Rollback / Dispose.

Every IInquiry method then routes via `ActivePipeline => _ambientSlot.Value?.Pipeline ?? _defaultPipeline`. Slot present → transacted pipeline reusing one connection. Slot absent or `Pipeline == null` → default pipeline opening a fresh connection per call. Generated stores (which hold the same DI-scoped `DefaultInquiry`) participate automatically without per-call wiring.

The nested case in `BeginTransactionAsync` short-circuits: if `_ambientSlot.Value?.Pipeline` is already set, it doesn't open a new physical transaction; it calls `outerPipeline.SaveSavepointAsync(name, ct)` and returns a `SavepointInquiryTransaction`. The slot stays pointing at the outer pipeline — savepoints share its physical connection.

### Concurrency guard

`DbConnection` isn't thread-safe. `TransactedInquiryRequestPipeline` serializes access with an `Interlocked.CompareExchange(ref _inFlight, 1, 0)` guard at the top of every operation. A second op starting while another is in flight throws `InvalidOperationException("Cannot start a new Inquiry operation while another operation is in flight on the same transaction.")` instead of corrupting the connection.

The savepoint primitives (`SaveSavepointAsync` / `ReleaseSavepointAsync` / `RollbackToSavepointAsync`) respect the same guard — they're SQL statements on the connection, just like data ops.

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
