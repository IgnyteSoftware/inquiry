# How Inquiry works

Inquiry is built on one core idea: **SQL is data that should be computed at build time, not at run time.** This page walks through the pipeline that makes that work.

## The compile-time pipeline

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Your assembly                                                              │
│                                                                             │
│  [assembly: InquiryDialect("Sqlite")]                                       │
│                                                                             │
│  [InquiryTable("Shippers")]              public partial class ShipperStore  │
│  public class Shipper { ... }              : InquiryStore<Shipper>          │
│                                          { [InquirySelectAll] ... }         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  Inquiry.<Dialect>.Analyzer (Roslyn incremental source generator)            │
│                                                                             │
│   1. Discover entities (classes with [InquiryTable])                        │
│   2. Discover stores   (partial classes : InquiryStore<T>)                  │
│   3. For each store method:                                                 │
│        - resolve columns / predicates / order keys                          │
│        - run the per-dialect SqlBuilder                                     │
│        - emit `const string _sql... = "...";`                               │
│        - emit a typed partial method body that calls the pipeline           │
│   4. Emit the entity materializer (struct + class variants)                 │
│   5. Emit InquiryGeneratedSchema.Ddl                                        │
│   6. Emit InquiryGeneratedServiceRegistration                               │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  Compiled assembly                                                           │
│                                                                             │
│  partial class ShipperStore                                                 │
│  {                                                                          │
│      private const string _sqlSelectAll = "SELECT ... FROM \"Shippers\"";   │
│      public partial Task<IReadOnlyList<Shipper>> SelectAllAsync(...)        │
│          => Inquiry.QueryListAsync<Shipper, ShipperStructMat>(...);         │
│  }                                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ at run time
┌─────────────────────────────────────────────────────────────────────────────┐
│  InquiryRequestPipeline                                                      │
│                                                                             │
│   1. OpenConnection (provider-specific factory)                             │
│   2. CreateCommand, set CommandText = baked _sql...                         │
│   3. Bind parameters via the per-method binder lambda                       │
│   4. ExecuteReaderAsync(CommandBehavior.SingleResult | SequentialAccess)    │
│   5. Read each row, call struct materializer (inlined per concrete type)    │
└─────────────────────────────────────────────────────────────────────────────┘
```

## What lives where

| Project | Role |
|---|---|
| `Inquiry` | Public runtime: `IInquiry` facade, request pipeline, attributes, command/parameter types, transactions, DI extension. **Ships zero SQL.** |
| `Inquiry.Generators.Shared` | The shared incremental generator framework — entity/store discovery, the per-dialect `SqlBuilder` hierarchy, the emitters. Bundled privately into each provider analyzer. |
| `Inquiry.<Dialect>.Analyzer` | The dialect's Roslyn analyzer. Wraps the shared framework with a `[Generator]` attribute that fires only when its dialect matches `[assembly: InquiryDialect]`. |
| `Inquiry.<Dialect>` | The runtime provider package: connection factory, DI extension (`AddInquirySqlite`, `AddInquirySqlServer`, …), the `[assembly: InquiryDialect]` marker. |

## Why one dialect per assembly

`[InquiryDialect]` is `AllowMultiple = false`. The generator emits one set of SQL per assembly — there's no runtime dialect dispatch. To target multiple databases, **split your entities across assemblies**, one per dialect.

This is intentional. Inquiry's whole performance story rests on the SQL being a `const string` — known at compile time, never built, never adapted at runtime. Allowing multi-dialect would require runtime dispatch and would forfeit that property.

## What the runtime actually does

For a `SELECT` (list read), the request pipeline is dramatically shorter than an ORM:

1. Open a connection (pooled by the provider).
2. Create a command.
3. Set `CommandText = _sqlSelectAll` (the const string baked at compile time).
4. Call the per-method binder lambda to add parameters (also generated).
5. `ExecuteReaderAsync` with `CommandBehavior.SingleResult | SequentialAccess` — stream forward-only.
6. Per row, call the per-call **struct** materializer. The JIT specializes per concrete materializer type, so the call inlines — no virtual dispatch.
7. Yield or accumulate.

There is no SQL building, no expression-tree compilation, no per-call reflection. The only allocations on the read path are the entities themselves and the `List<T>` (when buffered).

For a single-row read, the pipeline additionally passes `CommandBehavior.SingleRow`.

For inserts / updates / deletes (`ExecuteNonQuery`), the pipeline skips the reader entirely.

## Interceptors, transactions, prepared statements

The pipeline supports the usual cross-cutting features without forfeiting compile-time SQL:

- **Interceptors** (`IInquiryCommandInterceptor`) — observe / mutate the command before and after execution.
- **Transactions** — `IInquiry.BeginTransactionAsync()` installs an `AsyncLocal` slot pointing at a transacted pipeline that reuses one connection and one `DbTransaction`. Generated stores resolved from DI automatically join the open transaction via that slot; ad-hoc SQL goes through the `IInquiryTransaction` handle's own forwarding methods (`tx.ExecuteAsync(...)`, etc.). Nested `BeginTransactionAsync` creates savepoints (unbounded depth). The handle's forwarding methods throw `ObjectDisposedException` after the transaction closes. Full writeup: [Transactions](features/transactions.md).
- **Prepared statements** — opt-in via `InquiryOptions.PrepareStatements = PreparedStatementMode.Auto`. The pipeline calls `PrepareAsync` once per command, after which the database keeps the parsed plan for the lifetime of the connection.
- **Retry on transient cloud errors** — provider factories wrap connection opens with an exponential-backoff retry policy for known transient codes (Azure SQL, CockroachDB, Aurora, etc.).

## What the generator emits, per assembly

Counting from a single Northwind sample assembly:

- One **`*InquiryEntity.g.cs`** per `[InquiryTable]` entity — the class + struct materializers.
- One **`*Store.InquiryStore.g.cs`** per `[InquiryStore<T>]` partial class — the baked SQL consts and partial method bodies.
- One **`InquiryGeneratedSchema.g.cs`** — the full CREATE TABLE DDL for every entity, in dependency order, as a `const string`.
- One **`InquiryGeneratedServiceRegistration.g.cs`** — the DI registration class invoked by `AddInquiryGeneratedStores()`.

You can see these for yourself by adding `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to your csproj and rebuilding.

## Diagnostics

When you write something the generator can't handle — an unknown column name in a `[InquirySelectAllByField]`, a key the store doesn't have, an unsupported return shape — you get an `INQxxx` diagnostic at build time with the exact source location. No 3 AM debugging of a bad SQL string.

## See also

- [Getting started](getting-started.md) — the 5-minute walkthrough.
- [Architecture deep-dive](architecture.md) — the full pipeline and emitter internals.
- [CRUD feature page](features/crud.md) — input source side-by-side with the actual generated output.
