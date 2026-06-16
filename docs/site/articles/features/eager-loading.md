# Eager loading

Pull related entities alongside a parent with `[InquirySelectOneByKeyEager]` (one entity by key) or `[InquirySelectAllEager]` (all rows). The generator loads the parent, then runs **one additional query per relation** to populate each navigation property — a separate-query strategy, not a JOIN. Every navigation property declared with `[InquiryRelation]` on the entity is loaded.

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

The parent is fetched by key; then each relation is fetched in its own query and assigned to the navigation property (the relation queries run only when the parent is found):

```sql
-- 1. the parent, by key
SELECT "OrderID", "CustomerID" FROM "Orders" WHERE "OrderID" = @OrderID

-- 2. the Customer reference: the child by its key, bound to the parent's foreign key
SELECT "CustomerID", "CompanyName", ... FROM "Customers" WHERE "CustomerID" = @CustomerID
```

A **to-one reference** is fetched with `QuerySingleOrDefaultAsync` (as above); a **to-many collection** streams the children with `WHERE <childForeignKey> = <parentKey>` and accumulates them into the navigation list. An orphan or missing foreign key leaves the navigation property `null` (reference) or empty (collection).

> **One round-trip per relation.** A parent with *k* relations costs *k + 1* queries. Collapsing the parent and relation `SELECT`s into a single multi-result-set command (one round-trip) is a planned enhancement — see the [roadmap](../../develop/roadmap.md).

## Relation validation

`[InquiryRelation]` shapes are checked at **declaration time**, so a mistyped or reversed relation is caught even when no method eager-loads it:

- **`INQ040`** — the foreign-key property isn't a mapped column on the side that should own it (a typo).
- **`INQ058`** — the foreign-key property exists, but on the *opposite* side: a collection (to-many) relation's FK belongs to the child, a reference (to-one) relation's FK to the parent. This usually means the relation is declared backwards.
- **`INQ041`** — the related child entity has a composite primary key (eager loading supports single-key children in v1).

## Limitations

- **One level of include** in the current implementation — chained includes (`Order.OrderDetails[].Product`) are a future addition.
- **One round-trip per relation** — each relation is a separate query rather than a single JOIN; single-round-trip (multi-result-set) loading is on the [roadmap](../../develop/roadmap.md).
