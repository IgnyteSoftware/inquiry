# Transactions

Open a transaction on the `IInquiry` facade. Every operation inside the `using` scope — direct ad-hoc SQL on the transaction handle, or a method on a generated store resolved from DI — shares the same connection and the same `DbTransaction`. Commit when done, or let dispose roll it back.

## Usage

```csharp
var inquiry = sp.GetRequiredService<IInquiry>();
var customers = sp.GetRequiredService<CustomerStore>();
var orders = sp.GetRequiredService<OrderStore>();

await using var tx = await inquiry.BeginTransactionAsync();

// Generated stores see the transaction automatically — they were resolved before the
// transaction existed, but they share the ambient pipeline that the transaction installed.
await customers.InsertAsync(new Customer { … });
await orders.InsertAsync(new Order { … });

// Ad-hoc SQL: call directly on the transaction (forwarding overloads delegate to `tx.Inquiry`).
await tx.ExecuteAsync(
    "UPDATE Inventory SET Qty = Qty - @q WHERE SKU = @s",
    new { q = 1, s = sku });

await tx.CommitAsync();   // dispose without committing → automatic rollback
```

Disposing the transaction without committing rolls back. This is intentional — if your `using` block throws, the transaction unwinds cleanly.

## Two routing styles, one transaction

```csharp
// Style A — call methods directly on the transaction (forwarding overloads):
await tx.ExecuteAsync("UPDATE …");
var loaded = await tx.QuerySingleOrDefaultAsync<Customer>("SELECT … WHERE id = @id", new { id });

// Style B — call methods on tx.Inquiry (the underlying IInquiry handle):
await tx.Inquiry.ExecuteAsync("UPDATE …");
var loaded = await tx.Inquiry.QuerySingleOrDefaultAsync<Customer>("SELECT … WHERE id = @id", new { id });

// Style C — call methods on a generated store resolved from DI:
await customerStore.UpdateAsync(customer);   // routes through the ambient pipeline
```

All three styles produce identical SQL on the identical connection in the identical transaction. Style A is ergonomic for ad-hoc SQL; Style C is what you'll use most. Style B is the escape hatch for the few advanced overloads that aren't surfaced on `IInquiryTransaction` directly (the struct-materializer / TArgs binder paths generated stores use internally).

## How it works (the ambient mechanism)

`DefaultInquiry` holds an `AsyncLocal<AmbientTransactionSlot>` — a *holder reference*, not the pipeline directly. `BeginTransactionAsync`:

1. **Installs the slot synchronously**, before any await. (`AsyncLocal` values set inside an async callee don't flow back up to the caller — a documented .NET behavior — so the holder must exist before we await on opening the connection.)
2. Opens a fresh connection, calls `connection.BeginTransactionAsync(isolationLevel, ct)`, and fills in the holder's `Pipeline` field with a `TransactedInquiryRequestPipeline` wrapping the (connection, transaction) pair. The caller's async context already references the same holder → the post-await mutation is visible.
3. Every `IInquiry` method routes through `ActivePipeline = ambientSlot?.Pipeline ?? defaultPipeline`. Slot present → transacted pipeline reusing one connection. Slot absent → default pipeline opening a fresh connection per call.
4. On the first of **Commit / Rollback / Dispose**, an `onClose` callback nulls out the slot's `Pipeline` field. Straggler async work that fires after the transaction has closed silently falls through to the default pipeline — no use-after-dispose, no leak.

This is what lets generated stores (which were resolved from DI before any transaction existed) participate in transactions without a per-call parameter, a `WithTransaction` builder, or a re-resolution step.

## Isolation levels

```csharp
await using var tx = await inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
Console.WriteLine(tx.IsolationLevel);   // → Serializable
```

The level is passed through to `DbConnection.BeginTransactionAsync(isolationLevel, ct)`. The exact semantics are provider-dependent — each engine maps the .NET `IsolationLevel` enum to its native isolation modes. Read each provider's documentation for the engine-specific guarantees.

Default level (when omitted): `ReadCommitted`.

## Nested transactions — savepoints

`BeginTransactionAsync` called when an ambient transaction already exists *does not* start a second physical transaction. It creates a savepoint on the existing one.

```csharp
await using var outer = await inquiry.BeginTransactionAsync();
await outer.ExecuteAsync("INSERT INTO Audit (Event) VALUES ('start')");

await using (var inner = await outer.BeginTransactionAsync())   // SAVEPOINT inquiry_sp_1
{
    try
    {
        await ReallyRiskyWorkAsync(inner);
        await inner.CommitAsync();   // RELEASE SAVEPOINT inquiry_sp_1
    }
    catch
    {
        await inner.RollbackAsync(); // ROLLBACK TO SAVEPOINT inquiry_sp_1
        // The outer transaction continues — the audit row above stays.
    }
}

await outer.ExecuteAsync("INSERT INTO Audit (Event) VALUES ('end')");
await outer.CommitAsync();
```

Semantics:

- **Inner Commit** — releases the savepoint. The changes inside the savepoint stay part of the outer transaction.
- **Inner Rollback** — rolls back to the savepoint. Changes inside the savepoint are discarded; the outer transaction continues with its prior state.
- **Inner Dispose without Commit** — best-effort rollback to the savepoint.
- **Inner inherits outer's `IsolationLevel`.** You cannot change isolation mid-transaction on any provider; the argument to a nested `BeginTransactionAsync` is ignored.
- **Nesting is unbounded.** Each call gets a unique savepoint name (`inquiry_sp_<N>`).
- **Oracle quirk** — Oracle does not support explicit savepoint release. Inner Commit catches the resulting `NotSupportedException` and treats the savepoint as committed locally; Oracle implicitly cleans it up when the outer transaction commits.

The forwarding `BeginTransactionAsync` on `IInquiryTransaction` is sugar for `tx.Inquiry.BeginTransactionAsync(tx.IsolationLevel, ct)` — both create a savepoint.

## Concurrency inside a transaction

`DbConnection` isn't thread-safe. The transacted pipeline serializes operations with an `Interlocked.CompareExchange` guard: if a second operation starts while another is in flight on the same transaction, you get an explicit:

> `InvalidOperationException`: *Cannot start a new Inquiry operation while another operation is in flight on the same transaction. `DbConnection` is not thread-safe; serialize operations within a single transaction (no `Task.WhenAll`, no concurrent foreach).*

This catches the bug at the API boundary instead of letting it corrupt the connection.

Across **different** async flows / different transactions, there's no contention — the `AsyncLocal` slot keeps them isolated, and each has its own connection.

## What's not supported

- **DTC / cross-database transactions.** Out of scope for a single-engine ORM.
- **`TransactionScope` (System.Transactions).** Inquiry uses ADO.NET `DbTransaction` directly, not the ambient `Transaction.Current`. If you need to participate in a wider `TransactionScope` ambient, open it before resolving Inquiry and let the provider's connection auto-enlist (per its own ADO.NET behavior).

## See also

- [Concepts: how it works](../concepts.md) — the wider compile-time pipeline picture.
- [Architecture deep-dive](../architecture.md) — the runtime pipeline internals.
