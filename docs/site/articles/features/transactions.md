# Transactions

Open a transaction on the `IInquiry` facade. Direct calls on its transaction handle share one connection and `DbTransaction`. Generated stores enlist when they use the same scoped `IInquiry` and inherit its active async context. Stores from another DI scope do not enlist. Let `ExecuteInTransactionAsync` own the common commit/rollback flow, or use `BeginTransactionAsync` when you need manual rollback or savepoints.

## Usage

For most application code, use the owned helper:

```csharp
await using var scope = services.CreateAsyncScope();
var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
var customers = scope.ServiceProvider.GetRequiredService<CustomerStore>();
var orders = scope.ServiceProvider.GetRequiredService<OrderStore>();

await inquiry.ExecuteInTransactionAsync(async tx =>
{
    // Generated stores see the transaction automatically — they were resolved before the
    // transaction existed, but they share the ambient pipeline that the transaction installed.
    await customers.InsertAsync(new Customer { … });
    await orders.InsertAsync(new Order { … });

    // Ad-hoc SQL: call directly on the transaction handle.
    await tx.ExecuteAsync($"UPDATE Inventory SET Qty = Qty - {quantity} WHERE SKU = {sku}");
});
```

`ExecuteInTransactionAsync` commits when the delegate completes successfully. If the delegate throws, the transaction is disposed without committing and rolls back automatically. Use the generic overload when the unit of work should return a value:

```csharp
var customerId = await inquiry.ExecuteInTransactionAsync(async tx =>
{
    await customers.InsertAsync(customer);
    return customer.CustomerID;
});
```

When you need explicit rollback or a long-lived transaction handle, use `BeginTransactionAsync` directly:

```csharp
await using var tx = await inquiry.BeginTransactionAsync();
await customers.InsertAsync(new Customer { … });
await tx.ExecuteAsync($"UPDATE Inventory SET Qty = Qty - {quantity} WHERE SKU = {sku}");
await tx.CommitAsync();   // dispose without committing → automatic rollback
```

Disposing the transaction without committing rolls back. This is intentional — if your `using` block throws, the transaction unwinds cleanly.

## Two ways to make a transactional call

`IInquiryTransaction` is a flat, self-contained interface — there is no nested `Inquiry` handle to traverse. Two equivalent call styles:

```csharp
// Style A — call methods directly on the transaction handle:
await tx.ExecuteAsync($"UPDATE Customers SET LastSeenUtc = {now} WHERE CustomerID = {id}");
var loaded = await tx.QuerySingleOrDefaultAsync<Customer>($"SELECT * FROM Customers WHERE CustomerID = {id}");

// Style B — call methods on a generated store resolved from DI:
await customerStore.UpdateAsync(customer);   // routes through the ambient pipeline
```

Both use the same connection and transaction while the store shares the active scoped async context. Handle calls remain bound to their transaction even when a helper returns the handle to a caller that has no ambient slot.

### Use-after-close behavior

What happens if you call them *after* `CommitAsync` / `RollbackAsync` / `DisposeAsync`:

| Style | After close |
|---|---|
| **A. `tx.X(...)`** | ✅ Throws `ObjectDisposedException`. The transaction handle tracks its closed state and every query / execute method on it fails fast. |
| **B. `store.X(...)`** (generated store from DI) | Fresh calls after the transaction closes route to the non-transactional default pipeline. Async work that captured the transaction before it closed throws `ObjectDisposedException` if it resumes afterward. |

A store resolved from DI doesn't know about any individual transaction. It ambient-routes through whatever is in the `AsyncLocal` slot when called. Closing or disposing the handle detaches the current async flow. If a helper closes it, dispose the handle in the caller too; the caller's `await using` boundary performs that cleanup:

```csharp
await using (var tx = await inquiry.BeginTransactionAsync())
{
    await customers.InsertAsync(c1);    // in tx
    await CommitHelper(tx);
}
// Caller-side disposal clears its captured slot even though the helper already committed.
await customers.InsertAsync(c2);         // non-transactional, as expected

static async Task CommitHelper(IInquiryTransaction tx) => await tx.CommitAsync();
```

Before that caller-side disposal, a root or store call still sees the closed slot and throws. Repeated disposal detaches only that transaction's slot; it does not detach a newer transaction.

Async descendants that started inside the transaction keep their captured slot. If they resume after `CommitAsync`, `RollbackAsync`, or `DisposeAsync`, Inquiry fails fast instead of silently auto-committing outside the transaction. Await child work before closing the transaction, or start that work after the transaction scope exits.

## How it works (the ambient mechanism)

`DefaultInquiry` holds an `AsyncLocal<AmbientTransactionSlot>` — a *holder reference*, not the pipeline directly. `BeginTransactionAsync`:

1. **Installs the slot synchronously**, before any await. (`AsyncLocal` values set inside an async callee don't flow back up to the caller — a documented .NET behavior — so the holder must exist before we await on opening the connection.)
2. Opens a fresh connection, calls `connection.BeginTransactionAsync(isolationLevel, ct)`, and fills in the holder's `Pipeline` field with a `TransactedInquiryRequestPipeline` wrapping the (connection, transaction) pair. The caller's async context already references the same holder → the post-await mutation is visible.
3. Query and execute methods on the scoped facade check the ambient slot. Active slot → transacted pipeline reusing one connection. No slot → default pipeline opening a fresh connection per call. Closed captured slot → `ObjectDisposedException`.
4. On the first of **Commit / Rollback / Dispose**, Inquiry detaches the current async flow from the slot and then marks the captured holder closed. Disposal also detaches the calling flow when a helper already closed the handle. Fresh work after that boundary uses the default pipeline; async work still holding the closed slot fails fast.

This is what lets generated stores (which were resolved from DI before any transaction existed) participate in transactions without a per-call parameter, a `WithTransaction` builder, or a re-resolution step.

The `IInquiryTransaction` handle captures its transaction pipeline and checks its closed state on each direct call. Its routing does not depend on the caller's ambient slot.

## Cancellation and write outcomes

Cancellation does not prove that a write was undone. The database may have applied a command
before cancellation was observed. Do not retry a cancelled write blindly: first establish the
outcome, or use an application operation key that prevents duplicate effects.

With a caller-owned transaction, the caller owns the commit or rollback decision. Catching
`OperationCanceledException` does not mark the transaction rollback-only. If the intended unit
of work is incomplete, roll it back with a separate, usable cancellation token, or dispose it
without committing. A rollback failure leaves the outcome uncertain; retain that failure.

Let cancellation escape `ExecuteInTransactionAsync` to trigger disposal and rollback. If the
callback catches cancellation and returns normally, the helper attempts to commit. Outside an
explicit transaction there is no Inquiry transaction to roll back. Provider cancellation and
transaction usability differ; confirm the outcome before deciding to commit or retry.

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
var startEvent = "start";
await outer.ExecuteAsync($"INSERT INTO Audit (Event) VALUES ({startEvent})");

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

var endEvent = "end";
await outer.ExecuteAsync($"INSERT INTO Audit (Event) VALUES ({endEvent})");
await outer.CommitAsync();
```

Semantics:

- **Inner Commit** — releases the savepoint. The changes inside the savepoint stay part of the outer transaction.
- **Inner Rollback** — rolls back to the savepoint. Changes inside the savepoint are discarded; the outer transaction continues with its prior state.
- **Inner Dispose without Commit** — best-effort rollback to the savepoint.
- **Inner inherits outer's `IsolationLevel`.** You cannot change isolation mid-transaction on any provider.
- **Nesting is unbounded.** Each call gets a unique savepoint name (`inquiry_sp_<N>`).
- **Oracle quirk** — Oracle does not support explicit savepoint release. Inner Commit catches the resulting `NotSupportedException` and treats the savepoint as committed locally; Oracle implicitly cleans it up when the outer transaction commits.

## Enlisting external writes (outbox interop)

Messaging libraries with a transactional outbox — MassTransit, Wolverine, or your own — need to write their message rows on **the same connection and transaction** as your entity work, so the business change and the outgoing message commit or roll back atomically. The transaction handle exposes both for exactly this:

```csharp
await using var tx = await inquiry.BeginTransactionAsync();

await orderStore.InsertAsync(order);                       // Inquiry's work

await using (var cmd = tx.Connection.CreateCommand())      // the outbox's work
{
    cmd.Transaction = tx.Transaction;
    cmd.CommandText = "INSERT INTO OutboxMessages (Payload) VALUES (@p)";
    // ... bind, execute ...
    await cmd.ExecuteNonQueryAsync();
}

await tx.CommitAsync();                                    // both or neither
```

Rules of the road:

- **Borrowed, not owned.** Issue commands on `tx.Connection` / `tx.Transaction`, but never close, commit, roll back, or dispose them directly — the `IInquiryTransaction` handle owns their lifetime. Use `tx.CommitAsync()` / `tx.RollbackAsync()`.
- **Fail-fast after close.** Both properties throw `ObjectDisposedException` once the transaction is committed, rolled back, or disposed — same as every other member on the handle.
- **Savepoints share the pair.** A nested handle's `Connection` / `Transaction` are the *outer* transaction's — a savepoint is the same physical transaction.
- **Serialize access.** The same single-connection rule as everything else in a transaction applies (see below): don't run external commands concurrently with Inquiry operations on the same handle.

## Concurrency inside a transaction

`DbConnection` isn't thread-safe. The transacted pipeline serializes operations with an `Interlocked.CompareExchange` guard: if a second operation starts while another is in flight on the same transaction, you get an explicit:

> `InvalidOperationException`: *Cannot start a new Inquiry operation while another operation is in flight on the same transaction. `DbConnection` is not thread-safe; serialize operations within a single transaction (no `Task.WhenAll`, no concurrent foreach).*

This catches the bug at the API boundary instead of letting it corrupt the connection.

Across **different** async flows / different transactions, there's no contention — the `AsyncLocal` slot keeps them isolated, and each has its own connection.

## Row locks

To stop other transactions from changing rows you have read until your transaction ends, set
`LockMode` on the select. [Row locking](row-locking.md) lists the clause each provider uses and what
each mode does while another transaction holds the row.

## Application locks (SQL Server)

An application lock serializes work by name instead of by row, for example one import per tenant
at a time. On SQL Server, call `AcquireAppLockAsync` from `Ignyte.Inquiry.SqlServer` on the
transaction handle. It runs `sp_getapplock` with `@LockOwner = 'Transaction'` and checks the result:

```csharp
using Inquiry.SqlServer;

await inquiry.ExecuteInTransactionAsync(async tx =>
{
    await tx.AcquireAppLockAsync($"import:{tenantId}", SqlServerAppLockMode.Exclusive, TimeSpan.FromSeconds(10));

    // Only one transaction at a time runs this part for the same tenant.
    await RunImportAsync(tx, tenantId);
});
```

SQL Server releases the lock when the transaction commits or rolls back. A lock acquired through a
savepoint handle belongs to the outer transaction. If that savepoint rolls back, SQL Server also
releases a lock first acquired after the savepoint. It does not undo a mode change: a `Shared` lock
taken before the savepoint and converted to `Exclusive` after it stays `Exclusive` until the
transaction ends. There is no release method, and session-owned locks are not supported.

`resource` is 1 to 255 characters. SQL Server compares the name binary, so it is case-sensitive.
`timeout` is how long to wait: `TimeSpan.Zero` does not wait, and `Timeout.InfiniteTimeSpan` waits
until the lock is granted or the cancellation token is canceled. The helper sets the command timeout
to the lock timeout plus 30 seconds, so the client does not stop the wait early. An infinite lock
timeout sets no command timeout.

Each `sp_getapplock` return code has one outcome:

| Return code | Outcome |
|---|---|
| `0` | Granted at once. The call completes. |
| `1` | Granted after waiting for other holders to release it. The call completes. |
| `-1` | Not granted within the timeout. Throws `SqlServerAppLockException` with `Failure = Timeout`. |
| `-2` | SQL Server canceled the request. Throws `SqlServerAppLockException` with `Failure = Canceled`. |
| `-3` | Chosen as a deadlock victim. Throws `SqlServerAppLockException` with `Failure = Deadlock`. |
| `-999` | SQL Server rejected the parameters or the call. Throws `SqlServerAppLockException` with `Failure = InvalidRequest`. |

`SqlServerAppLockException` also exposes `Resource` and the raw `ReturnCode`. The exception means the
transaction did not get the requested mode. A lock it already held on the resource stays held: a
failed conversion from `Shared` to `Exclusive` keeps the `Shared` lock. The transaction stays
open after the exception, with the locks it already holds, so roll it back or let
`ExecuteInTransactionAsync` roll it back. When the caller's cancellation token stops the wait, the
call throws `OperationCanceledException`, as described in [Errors](../error-contracts.md).

The mode follows the SQL Server lock compatibility matrix: `Shared` holders coexist, `Exclusive`
blocks every other holder, and `Update`, `IntentShared` and `IntentExclusive` match their SQL Server
modes. Other providers' advisory locks, such as PostgreSQL `pg_advisory_xact_lock` and MySQL
`GET_LOCK`, have no helper.

## `TransactionScope` / System.Transactions

Inquiry's transaction model is ADO.NET `DbTransaction`, not the ambient `Transaction.Current`. That's a deliberate position, and here is exactly what it means for brownfield code that already uses `TransactionScope`:

- **Inquiry never opens or enlists a `TransactionScope` itself.** `BeginTransactionAsync` / `ExecuteInTransactionAsync` own a plain `DbTransaction` on a dedicated connection.
- **Operations *outside* an Inquiry transaction open one connection each.** If an ambient `TransactionScope` is active, whether that connection enlists is the **provider's** behavior, governed by its connection string (`Enlist=true` is the SqlClient/Npgsql default; MySqlConnector uses `AutoEnlist=true`; SQLite and Oracle have their own rules). Inquiry does not interfere either way.
- **The escalation trap:** because Inquiry opens a connection *per operation*, two Inquiry calls inside one `TransactionScope` enlist two connections. On SQL Server that promotes the transaction to a distributed (MSDTC) transaction — which throws on platforms without a DTC (i.e., Linux containers). This is the main reason to avoid mixing `TransactionScope` with multi-operation Inquiry work.
- **Recommended patterns:**
  - Use **Inquiry's own transaction** (`ExecuteInTransactionAsync`) for multi-operation atomicity — one connection, no escalation, stores enlist automatically.
  - If other libraries must share the unit of work, put *their* commands on Inquiry's transaction via the [outbox interop surface](#enlisting-external-writes-outbox-interop) (`tx.Connection` / `tx.Transaction`) instead of an ambient scope.
  - If an ambient `TransactionScope` is unavoidable, keep the Inquiry work inside it to a **single operation**, and verify your provider's enlistment setting.

An explicit opt-in enlistment API (`Connection.EnlistTransaction` on open) has been considered and remains unplanned unless there's demand — the patterns above cover the known cases without distributed-transaction risk.

## What's not supported

- **DTC / cross-database transactions.** Out of scope for a single-engine ORM.

## See also

- [Row locking](row-locking.md) — lock rows you read until the transaction ends.
- [Concepts: how it works](../concepts.md) — the wider compile-time pipeline picture.
- [Architecture deep-dive](../architecture.md) — the runtime pipeline internals.
