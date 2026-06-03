# Transactions

Open a transaction on the `IInquiry` facade; every store call inside it shares one connection and one `DbTransaction`. Commit or roll back when done.

## Usage

```csharp
var inquiry = provider.GetRequiredService<IInquiry>();

await using var tx = await inquiry.BeginTransactionAsync();

var orderStore = provider.GetRequiredService<OrderStore>();
var detailStore = provider.GetRequiredService<OrderDetailStore>();

await orderStore.InsertAsync(order);
foreach (var detail in details)
{
    await detailStore.InsertAsync(detail);
}

await tx.CommitAsync();
```

Disposing the transaction without committing rolls back.

## How it works

`BeginTransactionAsync` opens a single connection, starts a transaction, and pushes an `AsyncLocal<TransactedInquiryRequestPipeline>` that all stores see for the lifetime of the scope. Each `Inquiry.QueryListAsync` / `Inquiry.ExecuteAsync` call routes through the transacted pipeline, which:

- **Reuses the single connection** (instead of opening a fresh one per call).
- **Attaches the transaction** to every command.
- **Serializes operations** — a single connection isn't thread-safe, so a second in-flight call throws `InvalidOperationException` instead of corrupting state.

## Isolation levels

```csharp
await using var tx = await inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
```

The level is passed through to the underlying `DbConnection.BeginTransaction`. Provider semantics apply.

## Savepoints

Nested-transaction semantics use savepoints where the provider supports them (SQL Server, PostgreSQL, MySQL, Oracle). Sqlite has no savepoint support — nested calls throw at runtime.
