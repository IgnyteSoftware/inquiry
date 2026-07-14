# Many-to-many relations

Model a many-to-many association through a **junction (link) table** and eager-load the related collection — a single JOIN for one parent, or an N+1-free in-memory assembly for all parents. Mark a collection navigation with `[InquiryManyToMany]`, pointing at the mapped junction entity and its two foreign-key properties.

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

A `[InquiryManyToMany]` navigation must be a **collection** (`List<T>` / `IReadOnlyList<T>` / …), the **junction and related types must both be mapped** `[InquiryTable]` entities, the **junction must declare the two named foreign-key properties**, and the **related entity must have a single-column key**. Violations are a compile error (**`INQ063`**).

## Limitations (v1)

- The junction must be an explicitly mapped entity (no implicit/auto-managed junction table yet).
- The related (child) entity must have a single-column key.

## See also

- [Eager loading](eager-loading.md) — one-to-many / many-to-one relations.
- [CRUD](crud.md) — the operations the junction entity's store uses to write associations.
