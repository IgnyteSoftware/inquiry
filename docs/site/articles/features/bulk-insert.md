# Bulk insert

`[InquiryInsert]` with a collection runs batched DML in a transaction. `[InquiryBulkInsert]` is a
separate high-volume operation. It streams rows through the provider's native bulk-copy API instead of
binding each row as SQL parameters.

| Dialect | Mechanism |
|---|---|
| SQL Server | `SqlBulkCopy` (streaming) |
| PostgreSQL | binary `COPY … FROM STDIN` |
| MySQL, MariaDB | `MySqlBulkCopy` |
| SQLite, Oracle | compile-time fallback to the multi-row batch `INSERT` |

## You write

```csharp
public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<Shipper> shippers, CancellationToken ct = default);
}

long written = await store.BulkInsertAsync(MillionsOfRows());   // streams; no buffering, no parameter cap
```

The method takes `IEnumerable<T>` (lazy sequences stream end-to-end) and returns `Task<long>` — the rows written. An empty collection is a no-op returning 0.

## Semantics

- **Column set matches insert**: database-generated keys, database-default columns, and database-generated concurrency tokens are omitted; converters and enum mappings apply per value exactly as in single-row inserts.
- **Stamps still happen**: [sequential GUID keys](crud.md#key-generation-sequential-guids) and [auditing timestamps](auditing.md) are assigned per row as the stream is enumerated.
- **No parameter cap** on bulk-copy dialects — that's the point. The SQLite/Oracle fallback is the batch `INSERT` and keeps its cap; chunk accordingly there.
- **Transactional where the native API permits it**: see [Transactions and connection ownership](#transactions-and-connection-ownership).
- **Observable without row data**: native bulk insert emits a `BULK_INSERT` client span and duration metrics for the whole operation, connection open/acquisition, and native copy phase. Tags identify enlisted versus dedicated use, affected-row count, cancellation, and failures; values from inserted cells are never recorded. A dedicated connection that fails to open records a `connection.open_failed` event and an `error.type` tag on the open-duration metric, never `connection.opened`.
- **MySQL prerequisites**: `MySqlBulkCopy` uses `LOAD DATA LOCAL INFILE` under the hood. Inquiry enables `AllowLoadLocalInfile` **only on the dedicated bulk-insert connection** (never on regular pipeline connections — the flag widens what a SQL-injection bug could do, so it stays scoped), and the **server** must run with `local_infile=1`.

## Per-call options

Add an `InquiryBulkInsertOptions?` parameter before the cancellation token on a generated native bulk method when a caller needs tuning:

```csharp
[InquiryBulkInsert]
public partial Task<long> BulkInsertAsync(
    IEnumerable<Shipper> shippers,
    InquiryBulkInsertOptions? options,
    CancellationToken ct = default);
```

`Timeout`, `BatchSize`, `TableLock`, `NotifyAfter`/`RowsCopied`, and `ConnectionBehavior` are validated before copying. SQL Server supports every tuning option. PostgreSQL supports `Timeout`; its binary COPY API has no batch, table-lock, or progress option. MySQL/MariaDB support `Timeout` and progress, but not batch size or table locking. A provider throws `InvalidOperationException` naming an unsupported requested option before it opens a connection or enumerates rows. `ConnectionBehavior` can require an ambient transaction or require dedicated operation, preventing an accidental change in atomicity.

SQLite and Oracle compile `[InquiryBulkInsert]` to batch SQL. Their generated methods may accept the same parameter shape for cross-provider store interfaces, but reject any non-null native options before executing the fallback.

## Transactions and connection ownership

Outside an Inquiry transaction, a native bulk insert opens its own dedicated connection and disposes it
when the call ends. Inside an Inquiry transaction (including a [test sandbox](testing.md)), the
behavior depends on the provider:

| Provider | Inside an Inquiry transaction |
|---|---|
| SQL Server, PostgreSQL | Borrows the ambient connection and transaction. Rows commit or roll back with the caller's transaction. Inquiry never disposes the borrowed connection. |
| MySQL, MariaDB | Fails before writing, because regular connections deliberately omit `AllowLoadLocalInfile`. It enlists like SQL Server only when a custom connection factory enables that setting on the ambient connection. |
| SQLite, Oracle | The batch `INSERT` fallback always runs in the ambient transaction. |

Set `InquiryBulkInsertOptions.ConnectionBehavior` to `RequireAmbientTransaction` or
`RequireDedicatedConnection` to make the call fail, instead of silently changing its atomicity, when
the expected transaction is or is not active.

## Partial writes and retries

A failed native bulk insert can still leave rows in the table. Whether it does depends on where the
rows went:

- **Enlisted**: written rows belong to the caller's Inquiry transaction. Roll it back to discard them
  all; this is the supported way to make a whole bulk call all-or-nothing.
- **Dedicated, SQL Server with `BatchSize`**: `SqlBulkCopy` commits each batch on its own. If a later
  batch fails, the earlier batches stay committed. Without `BatchSize` the copy is one batch, and a
  row that the server rejects leaves no rows. Inquiry does not set
  `SqlBulkCopyOptions.UseInternalTransaction`; that option is also per batch and would not make the call
  atomic.
- **Dedicated, PostgreSQL**: the binary `COPY` is one statement, so a row that the server rejects leaves
  no rows.
- **MySQL, MariaDB warnings**: `LOAD DATA LOCAL INFILE` turns data problems, such as an overlong
  string, into warnings, and still writes the row (truncated or converted). Inquiry then throws
  `InquiryBulkInsertWarningException`, an `InvalidOperationException`. Its `RowsWritten` gives the
  server's row count and `Warnings` the messages. When `IsEnlisted` is `true`, the rows are in the
  caller's Inquiry transaction until it commits or rolls back. When it is `false`, they were written on
  a dedicated connection and are already committed. Telemetry records the copy as completed with that
  row count, and marks the operation as failed.

"Committed" above assumes no ambient `TransactionScope`. Inquiry does not track `System.Transactions`:
if the provider auto-enlists the dedicated connection in a scope (see
[TransactionScope](transactions.md#transactionscope--systemtransactions)), the rows commit or roll back
with that scope, and `IsEnlisted` is still `false`.

These no-row outcomes cover a copy that the database rejects. A call can also fail after the database
committed the rows: for example, a lost connection before the acknowledgement arrives, a throwing
telemetry listener, or a failed connection cleanup. Treat any failure of a dedicated call as uncertain.

Do not blindly retry a failed dedicated bulk insert: rows from the first attempt may be committed. To
retry safely, run the bulk insert inside an Inquiry transaction, load into a staging table that you
clear before each attempt, or remove the rows that the failed attempt wrote. See also
[Writes and uncertain outcomes](../error-contracts.md#writes-and-uncertain-outcomes).

## SQL Server tuning notes

`SqlBulkCopy` ships with defaults that are fine for small loads but can bite on large ones:

- **`BulkCopyTimeout` defaults to 30 seconds.** Set `InquiryBulkInsertOptions.Timeout` for larger loads.
- **`TableLock` is opt-in.** Set it only when the destination and workload make a table-level lock acceptable.

PostgreSQL binary `COPY` and `MySqlBulkCopy` expose only the options described above.

## When to use which tier

- 1–~2k rows: [`[InquiryInsert]`](batch-operations.md) — one multi-row `INSERT`, joins transactions and interceptors.
- More than that, or unbounded streams: `[InquiryBulkInsert]`.

The `Inquiry.Benchmarks.PostgreSql` project's `BulkInsertBenchmarks` compares the two tiers (chunked `VALUES` batches vs one binary `COPY`) head-to-head on a live server.

## See also

- [Batch operations](batch-operations.md) — the parameter-bound tier.
- [Auditing timestamps](auditing.md) / [CRUD key generation](crud.md#key-generation-sequential-guids) — per-row stamps that also apply here.
