# Bulk insert

`[InquiryBulkInsert]` is the 100k+-row tier above [batch operations](batch-operations.md): rows **stream** to the server through the provider's native bulk-copy API instead of being bound as SQL parameters.

| Dialect | Mechanism |
|---|---|
| SQL Server | `SqlBulkCopy` (streaming) |
| PostgreSQL | binary `COPY … FROM STDIN` |
| MySQL | `MySqlBulkCopy` |
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
- **Transactional where the native API permits it**: SQL Server and PostgreSQL reuse the open ambient Inquiry connection, participate in commit/rollback, and never dispose the borrowed connection. MySQL/MariaDB regular connections deliberately omit `AllowLoadLocalInfile`; those providers therefore fail before writing and point to `[InquiryInsertAll]`. They can enlist only when a custom ambient connection explicitly enables that setting. Outside a transaction, native bulk insert keeps using an independently owned connection. The SQLite/Oracle batch fallback already participates in ambient transactions.
- **Observable without row data**: native bulk insert emits a `BULK_INSERT` client span and duration metrics for the whole operation, connection open/acquisition, and native copy phase. Tags identify enlisted versus dedicated use, affected-row count, cancellation, and failures; values from inserted cells are never recorded.
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

## SQL Server tuning notes

`SqlBulkCopy` ships with defaults that are fine for small loads but can bite on large ones:

- **`BulkCopyTimeout` defaults to 30 seconds.** Set `InquiryBulkInsertOptions.Timeout` for larger loads.
- **`TableLock` is opt-in.** Set it only when the destination and workload make a table-level lock acceptable.

PostgreSQL binary `COPY` and `MySqlBulkCopy` expose only the options described above.

## When to use which tier

- 1–~2k rows: [`[InquiryInsertAll]`](batch-operations.md) — one multi-row `INSERT`, joins transactions and interceptors.
- More than that, or unbounded streams: `[InquiryBulkInsert]`.

The `Inquiry.Benchmarks.PostgreSql` project's `BulkInsertBenchmarks` compares the two tiers (chunked `VALUES` batches vs one binary `COPY`) head-to-head on a live server.

## See also

- [Batch operations](batch-operations.md) — the parameter-bound tier.
- [Auditing timestamps](auditing.md) / [CRUD key generation](crud.md#key-generation-sequential-guids) — per-row stamps that also apply here.
