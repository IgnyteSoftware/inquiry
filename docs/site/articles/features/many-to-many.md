# Many-to-many relations

Model a many-to-many association through a **junction (link) table** and eager-load the related collection — a single JOIN for one parent, or an N+1-free in-memory assembly for all parents. Mark a collection navigation with `[InquiryManyToMany]`, either pointing at a mapped junction entity and its foreign-key properties, or leaving it bare to have Inquiry [synthesize the junction](#auto-managed-junctions) for you.

## You write

```csharp
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = "";

    // Resolved through the OrderProduct junction: OrderId → this order, ProductId → the product.
    [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]
    public List<Product> Products { get; set; } = new();
}

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = "";
}

// The junction is an ordinary mapped entity (composite key of the two foreign keys).
[InquiryTable("OrderProduct")]
public sealed class OrderProduct
{
    [InquiryKey] public long OrderId { get; set; }
    [InquiryKey] public long ProductId { get; set; }
}

public partial class OrderStore : InquiryStore<Order>
{
    [InquirySelectOneByKeyEager]
    public partial Task<Order?> GetWithProductsAsync(long id, CancellationToken ct = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken ct = default);
}
```

`[InquirySelectOneByKeyEager]` / `[InquirySelectAllEager]` populate **every** relation on the entity, including the many-to-many ones.

## How it loads

The related rows are never carried on the child entity (the foreign keys live on the junction), so loading differs by shape:

- **Single parent** (`[InquirySelectOneByKeyEager]`) — one JOIN through the junction, filtered by the parent key:

  ```sql
  SELECT "Products"."Id", "Products"."Title"
    FROM "Products"
    INNER JOIN "OrderProduct" __j ON __j."ProductId" = "Products"."Id"
   WHERE __j."OrderId" = @Id
  ```

- **All parents** (`[InquirySelectAllEager]`) — parameterless child and junction result sets are assembled in memory, **no N+1**. The child query selects only rows whose key appears in an eligible junction row for an eligible parent; the junction query is scoped to the same parent set. Child, junction, and parent soft-delete/global filters are applied in SQL, so unrelated or filtered rows are not materialized. Both result sets reuse the existing child and junction materializers.

  The child query uses a child-key `IN` subquery rather than a correlated child-table scan:

  ```sql
  SELECT "Id", "Title"
    FROM "Products"
   WHERE "Id" IN (
       SELECT "__j"."ProductId"
         FROM "OrderProduct" "__j"
        WHERE "__j"."OrderId" IN (SELECT "Id" FROM "Orders")
  )
  ```

  Soft-delete and global-filter predicate terms are omitted from the example for brevity.

The JOIN is ANSI-standard, so the SQL is dialect-uniform across all six providers (the junction takes a space alias — Oracle rejects `AS` for table aliases — and child columns are table-qualified to stay unambiguous).

### Child filters

The related entity's own active-row predicate — its `[InquirySoftDelete]` term and every `[InquiryGlobalFilter]` term — is composed into **every** query that returns related rows: the single-parent JOIN, the batch child select, and the batch junction select. This holds for all three shapes, including a composite-key related entity and an auto-managed junction; a synthesized junction has no filter columns of its own, so the child's filter is the only thing that can exclude a link.

`IncludeDeleted = true` suppresses the **parent's** soft-delete term and nothing else. The child's soft-delete term still applies, and — as everywhere else — a global filter has no per-method opt-out, so it stays composed on both sides of an "include deleted" read.

## Writing associations

This attribute is read-side: it populates the navigation on eager loads. To **associate** or **dissociate**, write to the junction entity through its own store — the junction is a normal `[InquiryTable]`, so give it an `[InquiryInsert]` / `[InquiryDeleteOneByKey]`:

```csharp
public partial class OrderProductStore : InquiryStore<OrderProduct>
{
    [InquiryInsert] public partial Task<int> LinkAsync(OrderProduct link, CancellationToken ct = default);
}

await orderProducts.LinkAsync(new OrderProduct { OrderId = 1, ProductId = 42 });
```

## Rules

A `[InquiryManyToMany]` navigation must be a **collection** (`List<T>` / `IReadOnlyList<T>` / …), the **junction and related types must both be mapped** `[InquiryTable]` entities, and the **junction must name the parent foreign-key property plus one child foreign-key property per key column of the related entity**. Each rule reports its own compile error:

| Code | Reason |
|---|---|
| `INQ063` | The navigation is not a collection. |
| `INQ087` | The junction or related type is not a mapped `[InquiryTable]` entity. |
| `INQ088` | A named junction property is not a mapped column — the message names it. |
| `INQ089` | The number of child foreign keys does not match the related entity's key column count. For a **composite** key, also: duplicated names, or a foreign key whose type does not match the key column it is paired with. |

### Composite-key related entities

A related entity may have a composite key. Name one junction property per key column, **in the related entity's key-declaration order**:

```csharp
[InquiryManyToMany(typeof(PostTag), nameof(PostTag.PostId), nameof(PostTag.TenantId), nameof(PostTag.Slug))]
public List<Tag> Tags { get; set; } = new();
```

The pairing is positional, and both the generated SQL and the in-memory grouping follow it, so a transposed list is a silently wrong join rather than a compile error. `INQ089` rejects a composite pair whose types disagree, which catches most transpositions — but two key columns of the *same* type are indistinguishable, so **order still matters**. These pairing checks apply only to composite keys: with a single-column key there is one position and nothing to transpose, so a foreign key of a different (but comparable) type is accepted, as it always has been.

Composite keys correlate with a `EXISTS` subquery rather than a row-value `IN`: SQL Server has no row-value constructors, and row values would also impose a SQLite 3.15 floor. The shape is identical on all six providers.

## Auto-managed junctions

Leave the arguments off and Inquiry synthesizes the junction table itself — no junction entity to write:

```csharp
[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }

    [InquiryManyToMany]
    public List<Product> Products { get; set; } = new();
}
```

The generated DDL is an ordinary link table, named from both mapped tables and their key columns:

```sql
CREATE TABLE "Orders_Products" (
    "Orders_Id"   INTEGER NOT NULL,
    "Products_Id" INTEGER NOT NULL,
    PRIMARY KEY ("Orders_Id", "Products_Id"),
    FOREIGN KEY ("Orders_Id")   REFERENCES "Orders"("Id"),
    FOREIGN KEY ("Products_Id") REFERENCES "Products"("Id")
);
```

Names derive from an **order-independent** pair — the two tables sorted ordinally — so declaring the association from either side, or from both, describes the same table. Each side may declare one navigation; two separate associations between the same pair of entities need an explicitly mapped junction.

Override any of them with `JunctionTable`, `JunctionSchema`, `ParentColumn`, and `ChildColumn`. `JunctionTable` and `JunctionSchema` name the same object from either side, so state them identically. `ParentColumn` and `ChildColumn` are **relative to the declaring side** — `ParentColumn` is the column referencing the entity you are declaring on — so the reverse navigation states the two **swapped**:

```csharp
// on Order
[InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "order_id", ChildColumn = "product_id")]
public List<Product> Products { get; set; } = new();

// on Product — same table, columns swapped
[InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "product_id", ChildColumn = "order_id")]
public List<Order> Orders { get; set; } = new();
```

`INQ090` rejects the cases where a synthesized table would be wrong rather than merely unhelpful: a collision with a mapped entity's table, two sides disagreeing on the shape, a self-referential pair whose columns would collide, and a composite key that one column per side cannot express.

> [!IMPORTANT]
> Auto-managed junctions are **read-only**. Inquiry emits their DDL and eager-loads through them, but writing an association means inserting or deleting a junction row, which needs a mapped junction entity with its own store. Use the three-argument form when you need to write links, or issue raw SQL against the generated table.

> [!WARNING]
> A synthesized junction carries **only the two foreign keys** — no soft-delete column and no `[InquiryGlobalFilter]`. Reads are still correctly scoped, because every eager query composes the parent's and the child's own active-row filters, so a link whose endpoint is filtered out returns nothing. But the link rows themselves are not tenant-scoped and cannot be soft-deleted, and the raw-SQL escape hatch above is an unguarded write path into a table that carries no filter column. If your schema relies on soft delete or a tenant filter for links as well as for rows, map the junction explicitly so it can carry those columns.
>
> The generated foreign keys use `NO ACTION`. Where the provider enforces foreign keys, a hard `DELETE` of a parent that still has links is rejected by the constraint, so remove the links first. Where it does not — SQLite enforces them only when the connection issues `PRAGMA foreign_keys = ON`, which Inquiry does not do for you — the delete succeeds and orphan link rows remain.

## Limitations (v1)

- An auto-managed junction is read-only, and supports only single-column keys on both sides. Map the junction explicitly for either.

## See also

- [Eager loading](eager-loading.md) — one-to-many / many-to-one relations.
- [CRUD](crud.md) — the operations the junction entity's store uses to write associations.
