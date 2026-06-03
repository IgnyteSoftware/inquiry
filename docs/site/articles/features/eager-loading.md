# Eager loading

Pull related entities in a single query using `[InquiryInclude]`. The generator emits a `LEFT JOIN` to the related table and materializes the parent + child(ren) from one round-trip.

## You write

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    [InquirySelectOneByKey]
    [InquiryInclude(nameof(Order.Customer))]
    public partial Task<Order?> SelectByKeyWithCustomerAsync(int orderID, CancellationToken ct = default);
}
```

The `Customer` navigation property on `Order` must be declared with `[InquiryNavigation]` indicating the foreign-key column:

```csharp
[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey] public int OrderID { get; set; }
    [InquiryColumn] public string? CustomerID { get; set; }

    [InquiryNavigation(nameof(CustomerID))]
    public Customer? Customer { get; set; }
}
```

## The generator emits

```sql
SELECT o."OrderID", o."CustomerID", ..., c."CustomerID", c."CompanyName", ...
  FROM "Orders" AS o
  LEFT JOIN "Customers" AS c ON o."CustomerID" = c."CustomerID"
 WHERE o."OrderID" = @OrderID
```

A composed materializer reads both row halves, hydrating the parent and assigning the child to the navigation property.

## Limitations

- **One level of include** in the current implementation — chained includes (`Order.OrderDetails[].Product`) are a future addition.
- **One-to-many includes** materialize duplicates if multiple children match. The materializer de-duplicates the parent by primary key and accumulates the children.
