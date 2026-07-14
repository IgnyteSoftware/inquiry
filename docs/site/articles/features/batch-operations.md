# Batch operations

Insert, update, or delete many rows in a single round-trip. The generator emits one statement with a parameter list per row, so you get N rows for ~1 round-trip's worth of latency.

## You write

```csharp
public partial class ShipperStore : InquiryStore<Shipper>
{
    [InquiryInsertAll]
    public partial Task<int> InsertAllAsync(IReadOnlyList<Shipper> shippers, CancellationToken ct = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IReadOnlyList<Shipper> shippers, CancellationToken ct = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllByKeyAsync(IReadOnlyList<int> shipperIDs, CancellationToken ct = default);
}
```

## The generator emits

The SQL is **assembled at run time** per batch (the row count is unknown at compile time), but each row's parameter slot still uses the baked column list:

```csharp
// InsertAll: "INSERT INTO Shippers (CompanyName, Phone) VALUES (@CN_0, @Ph_0), (@CN_1, @Ph_1), ..."
// DeleteAllByKey: "DELETE FROM Shippers WHERE ShipperID IN (@K_0, @K_1, @K_2, ...)"
```

This is the one place Inquiry builds SQL at run time — necessarily, since the row count varies — but the column list and parameter shape come straight from the compile-time generator output.

## Converter-backed keys

`[InquiryDeleteAll]` takes key values in the entity's model type, including strongly typed IDs. Inquiry projects every non-null key through its configured value converter once, then passes the provider values to the dialect's existing collection transport (JSON table, native array, TVP, or expanded parameters). A null key element is not converted and cannot match a non-null primary key; empty and null collections remain zero-row no-ops. Converters use their cached singleton and a deferred static selector, with no intermediate collection allocation.

## How `UpdateAll` executes (ADO.NET `DbBatch`)

On SQL Server, `[InquiryDeleteAll]` and positive collection predicates use compile-time-generated, schema-qualified TVP artifacts with exact provider facets and nullability. Apply `InquiryGeneratedSchema.ProviderArtifactsDdl` in the migration/bootstrap path before the first call. Binding peeks once and streams a single enumerator; it creates no intermediate collection and performs no catalog query, DDL, or connection open. Null/empty inputs bind zero rows. Nullable elements remain `DBNull` rows rather than being removed.

PostgreSQL arrays and SQL Server TVPs normalize unsigned and `sbyte` elements to provider-supported numeric partners with unchecked bit-preserving casts. SQL Server uses `byte`/`TINYINT` for `sbyte`; PostgreSQL uses a `short[]` with the reinterpreted byte value `0..255` because Npgsql reserves `byte[]` for scalar `bytea`. Both use `short`, `int`, and `long` for `ushort`, `uint`, and `ulong`. Conversion happens once in the generated static selector, after any value converter. JSON-backed dialect transports are unchanged.

Batch update does **not** concatenate statements: each item runs the ordinary single-row
`UPDATE` (`_sqlUpdate`) with its own parameter set, dispatched through
`IInquiry.ExecuteBatchAsync`. On providers whose ADO.NET driver implements
`System.Data.Common.DbBatch` (Npgsql, Microsoft.Data.SqlClient, MySqlConnector) every row ships in
**one round trip**; elsewhere (SQLite, Oracle) the rows execute sequentially on a single
connection. Because each row is its own command, `UpdateAll` is no longer subject to the
per-command parameter cap, the SQL stays constant (prepared-statement friendly), and the row
count returned is the sum across items. `ExecuteBatchAsync` is also public on `IInquiry` for your
own repeated-statement workloads. Note: command interceptors fire per item only on the sequential
path; the `DbBatch` path has no per-command `DbCommand` to expose.

Like the multi-statement form before it, a batch is **not implicitly transactional** — wrap the
call in `ExecuteInTransactionAsync` if all-or-nothing semantics are required.

## Parameter limits

`InsertAll`, `DeleteAll`, and generated `IN` predicates stop before a command grows past `InquiryOptions.MaxParametersPerCommand` (default: `2000`). Lower it for providers or deployments with stricter limits; raise it only when your database and driver can handle larger commands reliably. (`UpdateAll` is exempt — see above.)

## Provider differences

- **PostgreSQL / SQLite** support multi-row `INSERT … VALUES (…), (…), …` directly.
- **SQL Server** uses the same multi-row VALUES. SQL Server caps one `INSERT ... VALUES` statement at
  1000 rows; Inquiry enforces `MaxParametersPerCommand` but does not auto-chunk, so chunk larger calls at
  the call site or lower the configured parameter cap for SQL Server workloads.
- **MySQL** supports multi-row VALUES with no hard cap (limited by `max_allowed_packet`).
- **Oracle** doesn't support multi-row VALUES; the generator emits `INSERT ALL` instead.
- **`UpdateAll` works on every provider**, including Oracle, via the `DbBatch`/sequential execution
  described above (the previous Oracle `INQ039` stub is gone).
