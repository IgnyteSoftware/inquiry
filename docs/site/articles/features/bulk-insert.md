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
- **Dedicated connection, no ambient transaction**: on native bulk-copy dialects, bulk insert uses its own provider connection, and interceptors/telemetry do not observe it. Inquiry rejects `[InquiryBulkInsert]` while an ambient Inquiry transaction is active because those writes could not participate in rollback. Use `[InquiryInsertAll]` for transaction-bound rows. If you need larger transactional bulk loads, load into a staging table outside the transaction and swap inside it. The SQLite/Oracle batch fallback participates in ambient transactions like any other batch insert.
- **MySQL prerequisites**: `MySqlBulkCopy` uses `LOAD DATA LOCAL INFILE` under the hood. Inquiry enables `AllowLoadLocalInfile` **only on the dedicated bulk-insert connection** (never on regular pipeline connections — the flag widens what a SQL-injection bug could do, so it stays scoped), and the **server** must run with `local_infile=1`.

## SQL Server tuning notes

`SqlBulkCopy` ships with defaults that are fine for small loads but can bite on large ones:

- **`BulkCopyTimeout` defaults to 30 seconds.** A bulk insert that exceeds this will throw a timeout exception. Inquiry does not override this default. If you're loading large datasets, consider chunking into smaller batches or increasing the server-side timeout at the connection/command level.
- **No `TableLock` option is exposed.** Without `SqlBulkCopyOptions.TableLock`, SQL Server acquires row-level locks and the insert is not minimally logged (even under the `SIMPLE` or `BULK_LOGGED` recovery model). For maximum throughput on an empty or dedicated table, a direct `SqlBulkCopy` call with `TableLock` will outperform the Inquiry path — use `[InquiryBulkInsert]` for convenience and type safety on moderate loads, and drop to raw ADO.NET when you need full control.

PostgreSQL binary `COPY` and `MySqlBulkCopy` do not have analogous timeout/locking knobs at the client API level.

## When to use which tier

- 1–~2k rows: [`[InquiryInsertAll]`](batch-operations.md) — one multi-row `INSERT`, joins transactions and interceptors.
- More than that, or unbounded streams: `[InquiryBulkInsert]`.

The `Inquiry.Benchmarks.PostgreSql` project's `BulkInsertBenchmarks` compares the two tiers (chunked `VALUES` batches vs one binary `COPY`) head-to-head on a live server.

## See also

- [Batch operations](batch-operations.md) — the parameter-bound tier.
- [Auditing timestamps](auditing.md) / [CRUD key generation](crud.md#key-generation-sequential-guids) — per-row stamps that also apply here.
