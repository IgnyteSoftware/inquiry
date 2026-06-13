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
- **Dedicated connection, no ambient transaction**: bulk insert does **not** join an open Inquiry transaction, and interceptors/telemetry do not observe it. If you need transactional bulk loads, load into a staging table and swap inside a transaction.
- **MySQL prerequisites**: `MySqlBulkCopy` uses `LOAD DATA LOCAL INFILE` under the hood — Inquiry's MySQL connection factory enables `AllowLoadLocalInfile` client-side, but the **server** must run with `local_infile=1`.

## When to use which tier

- 1–~2k rows: [`[InquiryInsertAll]`](batch-operations.md) — one multi-row `INSERT`, joins transactions and interceptors.
- More than that, or unbounded streams: `[InquiryBulkInsert]`.

The `Inquiry.Benchmarks.PostgreSql` project's `BulkInsertBenchmarks` compares the two tiers (chunked `VALUES` batches vs one binary `COPY`) head-to-head on a live server.

## See also

- [Batch operations](batch-operations.md) — the parameter-bound tier.
- [Auditing timestamps](auditing.md) / [CRUD key generation](crud.md#key-generation-sequential-guids) — per-row stamps that also apply here.
