# Aggregates

Return a single scalar value — `COUNT`, `SUM`, `AVG`, `MIN`, `MAX` — from your store. Pairs with `[InquiryWhere]` predicates for conditional aggregates.

## You write

```csharp
using Inquiry.Stores;

public partial class ProductStore : InquiryStore<Product>
{
    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken ct = default);

    [InquiryCount]
    [InquiryWhere("CategoryID", "=")]
    public partial Task<long> CountByCategoryAsync(int categoryID, CancellationToken ct = default);

    [InquiryAggregate(AggregateFunction.Sum, "UnitsInStock")]
    public partial Task<int?> TotalStockAsync(CancellationToken ct = default);

    [InquiryAggregate(AggregateFunction.Avg, "UnitPrice")]
    public partial Task<decimal?> AverageUnitPriceAsync(CancellationToken ct = default);
}
```

## The generator emits

```csharp
private const string _sqlCountAll          = "SELECT COUNT(*) FROM \"Products\"";
private const string _sqlCountByCategoryID = "SELECT COUNT(*) FROM \"Products\" WHERE \"CategoryID\" = @CategoryID";
private const string _sqlSumUnitsInStock   = "SELECT SUM(\"UnitsInStock\") FROM \"Products\"";
private const string _sqlAvgUnitPrice      = "SELECT AVG(\"UnitPrice\") FROM \"Products\"";
```

Each aggregate routes through `ExecuteScalarAsync<T>` — the return type drives how the result is converted.

## See also

- [Predicates](crud.md#crud) — `[InquiryWhere]` composition.
