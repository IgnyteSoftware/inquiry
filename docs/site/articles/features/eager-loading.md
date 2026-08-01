# Eager loading

Pull related entities alongside a parent with `[InquirySelectOneByKeyEager]` (one entity by key) or `[InquirySelectAllEager]` (all rows). The parent `SELECT` and every relation `SELECT` are batched into **a single multi-result-set command — one round trip** — and stitched in memory. It is a separate-`SELECT` strategy rather than a JOIN, so no parent column is duplicated across child rows. Every navigation property declared with `[InquiryRelation]` on the entity is loaded.

## You write

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    [InquirySelectOneByKeyEager]
    public partial Task<Order?> SelectByKeyWithCustomerAsync(int orderID, CancellationToken ct = default);
}
```

The `Customer` navigation property on `Order` is declared with `[InquiryRelation]`, naming the foreign-key property that links the two entities:

```csharp
[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey] public int OrderID { get; set; }
    [InquiryColumn] public string? CustomerID { get; set; }

    [InquiryRelation(nameof(CustomerID))]
    public Customer? Customer { get; set; }
}
```

## The generator emits

One command carrying both result sets, read in order through an `InquiryGridReader`:

```sql
-- result set 1: the parent, by key
SELECT "OrderID", "CustomerID" FROM "Orders" WHERE "OrderID" = @OrderID;

-- result set 2: the Customer reference, resolved server-side from the parent's foreign key
SELECT "CustomerID", "CompanyName", ... FROM "Customers"
WHERE "CustomerID" = (SELECT "CustomerID" FROM "Orders" WHERE "OrderID" = @OrderID)
```

A **to-one reference** resolves through that scalar subquery, so the foreign-key value never has to round-trip to the client first; a **to-many collection** filters with `WHERE <childForeignKey> = <parentKey>`. An orphan or missing foreign key leaves the navigation property `null` (reference) or empty (collection).

Oracle has no `;`-separated batch, so it wraps the same `SELECT`s in a `DBMS_SQL.RETURN_RESULT` PL/SQL block and returns them as implicit result sets. The client protocol — and the round-trip count — is identical.

> **A parent with *k* relations costs one round trip, not *k* + 1.** The only exception is a relation whose child type is not an `[InquiryTable]` entity: it is silently skipped, and its resolvable siblings still share the single command.

### `SelectAllEager` streams

`[InquirySelectAllEager]` orders the batch **children first, parent last**. The relation result sets fill the grouping dictionaries, then parents are materialized, stitched, and yielded one at a time — the parent set is never buffered into a list, so memory is bounded by the child rows rather than by the full result.

Two consequences:

- The reader stays open while the caller enumerates. Call `ToListAsync()` if the consumer is slow, or if it may abandon the loop early.
- Statements within one command are not read-consistent with each other outside a snapshot or repeatable-read transaction. Because the relation `SELECT`s run *before* the parent `SELECT`, a concurrent commit in between is visible in one direction only: a newly inserted parent arrives with an empty collection, and a **deleted child row is still attached** to its parent. The second case matters if you branch on a collection's contents — an authorization check like `resource.AccessGrants.Any(g => g.UserId == u)` can observe a grant that was already revoked. Wrap the call in a snapshot/repeatable-read transaction when the parent and its children must be read as of one instant.

  The size of that window is engine-dependent. Oracle runs the whole PL/SQL block server-side before any row reaches the client, so the gap is negligible; on SQL Server, SQLite, MySQL and MariaDB the next statement does not begin until the previous result set has been consumed, so the gap is as long as it takes to transfer the child rows.

## Relation validation

`[InquiryRelation]` shapes are checked at **declaration time**, so a mistyped or reversed relation is caught even when no method eager-loads it:

- **`INQ040`** — the foreign-key property isn't a mapped column on the side that should own it (a typo).
- **`INQ058`** — the foreign-key property exists, but on the *opposite* side: a collection (to-many) relation's FK belongs to the child, a reference (to-one) relation's FK to the parent. This usually means the relation is declared backwards.
- **`INQ041`** — the related child entity has a composite primary key (eager loading supports single-key children in v1).

## Limitations

- **One level of include** in the current implementation — chained includes (`Order.OrderDetails[].Product`) are a future addition.
- **Single-column keys only** on both sides — eager loading is rejected on a composite-key parent (`INQ012`) or child (`INQ041`).
