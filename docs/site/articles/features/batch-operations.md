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

    [InquiryDeleteAllByKey]
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

## Parameter limits

Batch methods and generated `IN` predicates stop before a command grows past `InquiryOptions.MaxParametersPerCommand` (default: `2000`). Lower it for providers or deployments with stricter limits; raise it only when your database and driver can handle larger commands reliably.

## Provider differences

- **PostgreSQL / SQLite** support multi-row `INSERT … VALUES (…), (…), …` directly.
- **SQL Server** uses the same multi-row VALUES, capped at 1000 rows per batch (then chunked).
- **MySQL** supports multi-row VALUES with no hard cap (limited by `max_allowed_packet`).
- **Oracle** doesn't support multi-row VALUES; the generator emits `INSERT ALL` instead.
- **`UpdateAll` on Oracle** is unsupported — the generator emits a throwing stub with `INQ039`.
