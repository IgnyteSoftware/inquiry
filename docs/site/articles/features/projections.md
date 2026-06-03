# Projections

Return a **column subset** as a DTO instead of materializing the full entity. Useful when you need only a few columns from a wide table — no wasted bandwidth, no wasted allocation.

## You write

Declare a projection type with `[InquiryProjection(typeof(SourceEntity))]`. Only properties marked `[InquiryColumn]` are included; they map to columns on the source entity by name.

```csharp
using Inquiry.Entities;

[InquiryProjection(typeof(Product))]
public sealed class ProductSummary
{
    [InquiryColumn] public int? ProductID { get; set; }
    [InquiryColumn] public string ProductName { get; set; } = "";
    [InquiryColumn] public decimal? UnitPrice { get; set; }
}

public partial class ProductStore : InquiryStore<Product>
{
    // Returns ProductSummary, not Product — the generator routes through the projection.
    [InquirySelectAll]
    public partial Task<IReadOnlyList<ProductSummary>> SelectSummariesAsync(CancellationToken ct = default);
}
```

## What the generator emits

A separate materializer for `ProductSummary` and SQL that selects only the projected columns:

```csharp
private const string _sqlSelectSummaries =
    "SELECT \"ProductID\", \"ProductName\", \"UnitPrice\" FROM \"Products\"";
```

## See also

- [CRUD](crud.md) — full-entity selects.
- [Aggregates](aggregates.md) — return a single scalar.
