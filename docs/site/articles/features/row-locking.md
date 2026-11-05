# Row locking

Set `LockMode` on a select to lock the rows it reads until your transaction ends. Use it when you read
a row, decide what to change, and write it back, and no other writer may change the row in between.
Use `UpdateSkipLocked` to let several workers take items from the same queue without waiting for each
other.

`LockMode` is available on `[InquirySelectOneByKey]`, `[InquirySelectAll]`, `[InquirySelectAllByField]`
and `[InquirySelectAllByPredicate]`. The default, `InquiryLockMode.None`, adds no lock clause.

## Modes and clauses

The generator adds one clause to the SELECT for the active provider:

| Mode | PostgreSQL | MySQL | MariaDB | SQL Server table hint | Oracle | SQLite |
|---|---|---|---|---|---|---|
| `Update` | `FOR UPDATE` | `FOR UPDATE` | `FOR UPDATE` | `WITH (UPDLOCK, ROWLOCK)` | `FOR UPDATE` | INQ039 |
| `UpdateNoWait` | `FOR UPDATE NOWAIT` | `FOR UPDATE NOWAIT` | `FOR UPDATE NOWAIT` | `WITH (UPDLOCK, ROWLOCK, NOWAIT)` | `FOR UPDATE NOWAIT` | INQ039 |
| `UpdateSkipLocked` | `FOR UPDATE SKIP LOCKED` | `FOR UPDATE SKIP LOCKED` | `FOR UPDATE SKIP LOCKED` | `WITH (UPDLOCK, ROWLOCK, READPAST)` | `FOR UPDATE SKIP LOCKED` | INQ039 |
| `Share` | `FOR SHARE` | `FOR SHARE` | `LOCK IN SHARE MODE` | `WITH (HOLDLOCK, ROWLOCK)` | INQ039 | INQ039 |

SQLite has no row locks, so every mode other than `None` fails the build with
[INQ039](../diagnostics.md#inq039). Oracle has no shared row lock, so `Share` fails the same way there.

## What each mode does

These results come from the integration tests that run the example below against each engine. The
tests use `ExecuteInTransactionAsync` and `BeginTransactionAsync` with their default isolation level,
`ReadCommitted`, and another transaction holding the lock:

| Mode | While another transaction holds the row |
|---|---|
| `Update` | The read waits until the other transaction ends. After a commit it returns the committed row. |
| `UpdateNoWait` | The read fails at once with the provider's lock error. See the next table. |
| `UpdateSkipLocked` | The read returns the matching rows that are not locked and leaves the locked row out. |
| `Share` | Another `Share` read of the same row succeeds. A write to the row waits until every shared lock ends. |

`UpdateNoWait` raises the provider's own exception:

| Provider | Exception | Error |
|---|---|---|
| PostgreSQL | `PostgresException` | SQLSTATE `55P03` |
| MySQL | `MySqlException` | `Number` 3572 |
| MariaDB | `MySqlException` | `Number` 1205 |
| SQL Server | `SqlException` | `Number` 1222 |
| Oracle | `OracleException` | `Number` 54 (ORA-00054) |

On SQL Server, an `Update` or `UpdateNoWait` read succeeds while another transaction holds a `Share`
lock on the row; only the later write waits. On PostgreSQL, MySQL and MariaDB the `UpdateNoWait` read
fails with the error above.

## Lock lifetime

A lock taken inside a transaction lasts until that transaction commits, rolls back, or is disposed
without committing. The transaction is the one on the `IInquiry` from the same DI scope as the store;
see [Transactions](transactions.md). A store from another DI scope runs on its own connection and does
not share the lock.

Outside a transaction, a locking select runs without error on all five engines, but the lock ends when
the statement does. Another transaction can lock the same row straight after the call returns. Always
take a lock inside the transaction that does the dependent write.

## Example: read, change, write

The example uses a work-item table. The files below are compiled and run against each provider by the
integration tests. They assume `ImplicitUsings` is enabled, the default for new .NET projects.

**WorkItem.cs**

<!-- source: tests/Inquiry.FeatureCatalog/Locking/WorkItem.cs -->
```csharp
using Inquiry.Entities;

namespace Fulfillment;

[InquiryTable("WorkItem")]
public sealed class WorkItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public int QueueId { get; set; }

    [InquiryColumn]
    public int State { get; set; }

    [InquiryColumn]
    public int Attempts { get; set; }
}
```

**WorkItemStore.cs**

<!-- source: tests/Inquiry.FeatureCatalog/Locking/WorkItemStore.cs -->
```csharp
using Inquiry.Stores;

namespace Fulfillment;

public partial class WorkItemStore : InquiryStore<WorkItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(WorkItem item, CancellationToken ct = default);

    [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
    public partial Task<WorkItem?> GetForUpdateAsync(long id, CancellationToken ct = default);

    [InquirySelectAllByPredicate(LockMode = InquiryLockMode.UpdateSkipLocked)]
    [InquiryWhere("QueueId", Parameter = nameof(queueId))]
    [InquiryWhere("State", Parameter = nameof(state))]
    public partial Task<IReadOnlyList<WorkItem>> AvailableAsync(
        int queueId, int state, CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(WorkItem item, CancellationToken ct = default);
}
```

**WorkItemService.cs**

<!-- source: tests/Inquiry.FeatureCatalog/Locking/WorkItemService.cs -->
```csharp
using Inquiry;
using Microsoft.Extensions.DependencyInjection;

namespace Fulfillment;

public static class WorkItemService
{
    public const int Pending = 0;
    public const int Claimed = 1;

    public static async Task<bool> RecordAttemptAsync(
        IServiceProvider services, long id, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var items = scope.ServiceProvider.GetRequiredService<WorkItemStore>();

        return await inquiry.ExecuteInTransactionAsync(async (_, token) =>
        {
            // The row stays locked until the transaction ends, so no other
            // writer can change Attempts between this read and the update.
            var item = await items.GetForUpdateAsync(id, token);
            if (item is null)
            {
                return false;
            }

            item.Attempts++;
            return await items.UpdateAsync(item, token);
        }, cancellationToken: ct);
    }

    public static async Task<IReadOnlyList<long>> ClaimAvailableAsync(
        IServiceProvider services, int queueId, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var items = scope.ServiceProvider.GetRequiredService<WorkItemStore>();

        return await inquiry.ExecuteInTransactionAsync<IReadOnlyList<long>>(async (_, token) =>
        {
            // Rows that another transaction has locked are skipped, not waited for.
            var available = await items.AvailableAsync(queueId, Pending, token);
            foreach (var item in available)
            {
                item.State = Claimed;
                await items.UpdateAsync(item, token);
            }

            return available.Select(item => item.Id).ToList();
        }, cancellationToken: ct);
    }
}
```

Each method creates its own DI scope, so it works from a background service or a request handler alike.
It resolves the facade and the store from that scope so the store enlists in the transaction.

`RecordAttemptAsync` locks one row with `Update`, increments `Attempts` and writes it back. If another
transaction already holds the row, `GetForUpdateAsync` waits for it and then reads the committed value.
In the tests, another transaction locks the row first and `RecordAttemptAsync` waits. The other
transaction then increments `Attempts` to 1 and commits, and `RecordAttemptAsync` reads 1 and writes 2.
No update is lost.

## Example: claim queued work

`ClaimAvailableAsync` reads the pending items of one queue with `UpdateSkipLocked`, marks them claimed
and commits. Items that another transaction has locked are skipped instead of waited for. In the tests,
with one item locked by another transaction, two free items in the same queue, and one item in another
queue, the call claims exactly the two free items. Once the other transaction ends, the next call claims
the item it held.

Keep these limits in mind when you adapt it:

- It claims every available item in the queue. It has no batch size, and the rows come back in no
  particular order.
- An empty result means nothing was claimable at that moment. Locked items may still be pending.
- Process the claimed items after the transaction commits. The example does not retry or release
  claims if processing fails.
- The test table has an index on `(QueueId, State)`.

## Limitations

The generator accepts the following combinations, but they do not lock as written. Avoid them:

- **`Distinct = true`** with a lock mode fails at run time on PostgreSQL (SQLSTATE `0A000`) and Oracle
  (ORA-01786).
- **`Paged = true`** with a lock mode fails at run time on Oracle (ORA-02014). PostgreSQL and SQL
  Server ran the paged query in the same probe.
- **A projection return type** on an `[InquirySelectAll]` method without `OrderBy` or `Paged`, such as
  `Task<IReadOnlyList<WorkItemSummary>>`, runs a SELECT without the lock clause, so no rows are locked.
  This was seen in the generated SQL for PostgreSQL, SQL Server and Oracle.
- **SQL Server, a column named like its table**: the table hint is inserted after the first occurrence
  of the table name, so a `[Status]` column in a `[Status]` table produces invalid SQL.

## Tested engines

| Provider | Image |
|---|---|
| PostgreSQL | `postgres:16-alpine` |
| MySQL | `mysql:8.4` |
| MariaDB | `mariadb:11.4` |
| SQL Server | SQL Server 2022 CU14, `READ_COMMITTED_SNAPSHOT` off (the default) |
| Oracle | Oracle Database 21c Express Edition (`gvenzl/oracle-xe`) |

## See also

- [Transactions](transactions.md): opening a transaction and enlisting generated stores.
- [Optimistic concurrency](concurrency.md): detect conflicting writes with a version column instead of
  holding a lock.
