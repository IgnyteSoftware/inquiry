# Eager loading

Pull related entities alongside a parent with `[InquirySelectOneByKeyEager]` (one entity by key) or `[InquirySelectAllEager]` (all rows). The parent `SELECT` and every relation `SELECT` are batched into **a single multi-result-set command — one round trip** — and stitched in memory. It is a separate-`SELECT` strategy rather than a JOIN, so no parent column is duplicated across child rows. By default, an eager method loads every navigation property declared with `[InquiryRelation]` or `[InquiryManyToMany]` on the entity. To load only some of them, name them with [`[InquiryInclude]`](#load-only-some-relations).

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

The streaming form of `[InquirySelectAllEager]` returns `IAsyncEnumerable<TEntity>`. It orders the batch **children first, parent last**. The relation result sets fill the grouping dictionaries, then parents are materialized, stitched, and yielded one at a time — the parent set is never buffered into a list, so memory is bounded by the child rows rather than by the full result.

Two consequences:

- The reader stays open while the caller enumerates. Declare the [buffered list form](#return-a-buffered-list) if the consumer is slow, or if it may abandon the loop early.
- Statements within one command are not read-consistent with each other outside a snapshot or repeatable-read transaction. Because the relation `SELECT`s run *before* the parent `SELECT`, a concurrent commit in between is visible in one direction only: a newly inserted parent arrives with an empty collection, and a **deleted child row is still attached** to its parent. The second case matters if you branch on a collection's contents — an authorization check like `resource.AccessGrants.Any(g => g.UserId == u)` can observe a grant that was already revoked. Wrap the call in a snapshot/repeatable-read transaction when the parent and its children must be read as of one instant.

  The size of that window is engine-dependent. Oracle runs the whole PL/SQL block server-side before any row reaches the client, so the gap is negligible; on SQL Server, SQLite, MySQL and MariaDB the next statement does not begin until the previous result set has been consumed, so the gap is as long as it takes to transfer the child rows.

## Load only some relations

Loading every relation is the default, and it stays the default. Add `[InquiryInclude]` to an eager method to load only the relations it names. Apply it once per relation:

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    // Default: loads Customer, OrderDetails and every other relation on Order.
    [InquirySelectOneByKeyEager]
    public partial Task<Order?> SelectByKeyWithRelationsAsync(int orderID, CancellationToken ct = default);

    // Loads Customer only.
    [InquirySelectOneByKeyEager]
    [InquiryInclude(nameof(Order.Customer))]
    public partial Task<Order?> SelectByKeyWithCustomerAsync(int orderID, CancellationToken ct = default);
}
```

The command for a selective method carries the parent `SELECT` and the `SELECT`s for each named relation: one for a reference or collection, and, in `[InquirySelectAllEager]`, two for a many-to-many relation (the related rows and the junction keys). Relations that the method does not name are not queried. Their properties keep the value the entity's constructor gives them, such as `null` or an empty list. If you add a relation to the entity later, the SQL, result graph and command count of a selective method stay the same. Methods without `[InquiryInclude]` load the new relation too.

The order of the attributes does not matter. Relations are read in the order the entity declares them. The build fails with **`INQ107`** when a name is not a relation on the entity, when the relation's type is not an `[InquiryTable]` entity, when the method names a relation twice, or when the attribute is on a method that is not an eager method.

Selection is one level deep. Nested paths such as `Order.OrderDetails[].Product` are not supported.

## Return a buffered list

`[InquirySelectAllEager]` can return `Task<IReadOnlyList<TEntity>>` as well as `IAsyncEnumerable<TEntity>`:

```csharp
[InquirySelectAllEager]
public partial Task<IReadOnlyList<Category>> SelectAllWithProductsAsync(CancellationToken ct = default);
```

The list form sends the same command as the streaming form. It applies the same filters, issues one command, and builds the same graph. It reads every parent into a list, closes the reader, and then completes the task. Use it when the caller needs the whole result anyway, for example to return it from an API. Use the streaming form when you process parents one at a time and do not keep them.

| | `IAsyncEnumerable<TEntity>` | `Task<IReadOnlyList<TEntity>>` |
|---|---|---|
| Commands | One | One |
| Child rows | Held in memory for the whole enumeration | Held in memory until the task completes |
| Parent rows | One at a time, unless the caller keeps them | All of them, in the returned list |
| Reader | Open while the caller enumerates | Closed before the task completes |
| Cancellation | The token passed to the method or to `WithCancellation` | The token passed to the method |
| Stopping early | Disposing the enumerator closes the reader | Not possible: the task reads every row |

Both forms keep every child row in memory because they read children before parents. The list form adds one list entry for each parent. When the task faults or is cancelled, the reader is closed and no partial list is returned.

## Relation validation

`[InquiryRelation]` shapes are checked at **declaration time**, so a mistyped or reversed relation is caught even when no method eager-loads it.

The foreign key is named by its **C# property name, matched exactly**. The column name and other casings do not match, even when they differ only in case. Validation, the generated SQL, and parameter binding all use this one rule, so `[InquiryColumn("region_id")] public int RegionRef` is named `nameof(RegionRef)`. Suppressing a relation diagnostic does not make the relation load: no eager loader is generated for it.

- **`INQ040`** — the foreign-key property isn't a mapped property on the side that should own it (a typo, or a column name). When a column name or another casing would have matched, the message names the property to use.
- **`INQ058`** — the foreign-key property exists, but on the *opposite* side: a collection (to-many) relation's FK belongs to the child, a reference (to-one) relation's FK to the parent. This usually means the relation is declared backwards.
- **`INQ041`** — the related child entity has a composite primary key (eager loading supports single-key children in v1).
- **`INQ104`** — the foreign key and the key it references store values differently, so the join could never match: an enum stored as text against the same enum (or any number) stored as a number, or two different value converters. Joining across different converters is not supported in 1.0; map both columns the same way. A converter on one side only is not checked, because whether its stored value matches the other column depends on the provider; typed IDs converted to `int` with a plain integer foreign key stay valid.

`byte[]` keys compare by value when related rows are matched in memory.

## Limitations

- **One level of include** in the current implementation — chained includes (`Order.OrderDetails[].Product`) are a future addition. `[InquiryInclude]` names relations on the store's entity only.
- **Single-column keys only** on both sides — eager loading is rejected on a composite-key parent (`INQ012`) or child (`INQ041`).
