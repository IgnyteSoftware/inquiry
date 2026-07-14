# Ad-hoc DTOs

Map a hand-written reporting query into a plain DTO — **no entity, no store, no table mapping**. This is the "I'd just use Dapper for this one query" escape hatch, kept inside Inquiry: mark the result type with `[InquiryAdHoc]` and the ad-hoc `IInquiry.Query*` methods materialize it.

Use an ad-hoc DTO when the result shape is not a row of one table: aggregates, multi-join reports, `GROUP BY` summaries. For a column subset of a single entity, prefer a [projection](projections.md); for a single value, use a [scalar aggregate](aggregates.md).

## You write

A plain class or record — properties need no attributes:

```csharp
using Inquiry.Entities;

[InquiryAdHoc]
public sealed class CategorySales
{
    public string Category { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public int OrderCount { get; set; }
}
```

Then query it through the `IInquiry` facade with interpolated SQL (each hole becomes a bound parameter — see [Security](../security.md)):

```csharp
var since = new DateTime(2026, 1, 1);

IReadOnlyList<CategorySales> report = await inquiry.QueryListAsync<CategorySales>(
    $"""
     SELECT c.CategoryName, SUM(od.UnitPrice * od.Quantity), COUNT(DISTINCT o.OrderID)
     FROM Orders o
     JOIN "Order Details" od ON od.OrderID = o.OrderID
     JOIN Products p ON p.ProductID = od.ProductID
     JOIN Categories c ON c.CategoryID = p.CategoryID
     WHERE o.OrderDate >= {since}
     GROUP BY c.CategoryName
     """);
```

`QueryAsync<T>` (streaming) and `QuerySingleOrDefaultAsync<T>` take the same DTOs.

## The ordinal contract

Mapping is **by position, not by name** — the same ordinal reads entity materializers use. Every public (or internal) instance property with a `set` or `init` accessor maps to one SELECT-list position, **in declaration order**. Column names and aliases in the SQL are irrelevant.

- Get-only (computed), static, and privately-settable properties are skipped and do not occupy an ordinal.
- The SELECT list must have at least as many columns as the DTO has mapped properties, in matching order and compatible types.
- `[InquiryEnumAsString]` is honored on enum properties; other enums read as their underlying integer.
- `Guid`, `DateOnly`/`TimeOnly`, and nullable types all read exactly as they do on entities.

```csharp
[InquiryAdHoc]
public sealed record SaleNote          // records work — use init properties,
{                                      // not positional parameters
    public long Id { get; init; }      // ordinal 0
    public string? Note { get; init; } // ordinal 1 (DBNull → null)
}
```

## What the generator emits

For each `[InquiryAdHoc]` type: a materializer reading the properties by ordinal, in a `{MetadataIdentity}.InquiryAdHoc.g.cs` file (namespace-qualified, with containing types separated by `+`, and generic arity backticks preserved as part of the CLR metadata identity, such as ``Outer`1+Inner`2``), plus a registration line in `AddInquiryGeneratedStores()`:

```csharp
internal sealed class CategorySalesInquiryAdHocMaterializer
    : IInquiryEntityMaterializer<CategorySales>
{
    public CategorySales Materialize(DbDataReader reader)
    {
        return new CategorySales
        {
            Category = reader.GetString(0),
            TotalAmount = reader.GetDecimal(1),
            OrderCount = reader.GetInt32(2),
        };
    }
}
```

That DI registration is what the ad-hoc `IInquiry.Query*` path resolves at runtime — without it, querying an unregistered type throws. Registration goes through the same `AddInquiryGeneratedStores()` (or assembly-scanning `AddInquiry(Assembly[])`) call you already make; nothing extra to wire.

## Constraints

- The type must be a **concrete class** (records included) with an accessible **parameterless constructor**. A positional record (`record RegionTotal(string Region, decimal Total)`) has only its primary constructor, so it is rejected with `INQ046` — use init-only properties instead.
- A type with no mappable properties is rejected with `INQ045`.
- Ad-hoc DTOs are read shapes only: they generate no DDL, no mutations, and cannot be used as store entity types.

## See also

- [Projections](projections.md) — column subsets of one entity, with generated SQL.
- [Aggregates](aggregates.md) — single scalar results.
- [Security](../security.md) — how interpolated ad-hoc SQL is parameterized.
