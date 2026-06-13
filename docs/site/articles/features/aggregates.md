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

## Existence checks

`[InquiryExists]` returns `Task<bool>` — the `EXISTS` / EF `.AnyAsync()` analog. It short-circuits at the first match, so it's cheaper than a `COUNT(*) > 0`. Apply zero or more `[InquiryWhere]` criteria (exactly as on a predicate select); with none, it tests whether the table has any row at all.

```csharp
[InquiryExists]
public partial Task<bool> AnyAsync(CancellationToken ct = default);

[InquiryExists]
[InquiryWhere("Name")]
public partial Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
```

```csharp
private const string _sqlExists_AnyAsync =
    "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"Products\") THEN 1 ELSE 0 END";
private const string _sqlExists_ExistsByNameAsync =
    "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"Products\" WHERE \"Name\" = @Name) THEN 1 ELSE 0 END";
```

The `CASE WHEN EXISTS(…) THEN 1 ELSE 0 END` form is portable across SQLite / SQL Server / PostgreSQL / MySQL (Oracle appends `FROM DUAL`); the resulting `1`/`0` is coerced to `bool`. Like the aggregates, the inner test composes the active-row filter — a [soft-deleted](soft-delete.md) or [globally-filtered](global-filters.md) row doesn't count as existing.

## See also

- [Predicates](crud.md#crud) — `[InquiryWhere]` composition.
