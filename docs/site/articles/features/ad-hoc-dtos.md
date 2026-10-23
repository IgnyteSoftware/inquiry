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
     SELECT c.CategoryName AS Category,
            SUM(od.UnitPrice * od.Quantity) AS TotalAmount,
            COUNT(DISTINCT o.OrderID) AS OrderCount
     FROM Orders o
     JOIN "Order Details" od ON od.OrderID = o.OrderID
     JOIN Products p ON p.ProductID = od.ProductID
     JOIN Categories c ON c.CategoryID = p.CategoryID
     WHERE o.OrderDate >= {since}
     GROUP BY c.CategoryName
     """);
```

`QueryAsync<T>` (streaming) and `QuerySingleOrDefaultAsync<T>` take the same DTOs.

## Column mapping

Mapping is **by name, not by position**. Every public (or internal) instance property with a `set` or `init` accessor reads the result column that has the same name. Use a SQL alias when the column name differs from the property name. The SELECT-list order does not matter.

- Names compare without regard to case (`StringComparison.OrdinalIgnoreCase`), so `ordercount`, `ORDERCOUNT`, and `OrderCount` all match `OrderCount`. Providers that fold unquoted aliases to lower or upper case need no quoting.
- Extra result columns are ignored. Reports can return helper columns that the DTO does not declare.
- A missing column throws `InvalidOperationException` before the first row is returned, even when the result has no rows. The message names the DTO, the property, and the available columns.
- If two result columns match one property, for example `Total` and `TOTAL`, the read throws. Inquiry does not pick one.
- Two properties whose names differ only by case, such as `Id` and `ID`, report build error `INQ106`.
- A database `NULL` in a non-nullable property throws `InvalidOperationException` that names the property. A column whose type the property cannot read throws `InvalidOperationException` that names the property and the column. The provider's exception is the inner exception.
- Get-only (computed), static, and privately-settable properties are skipped and need no column.
- `[InquiryEnumAsString]` is honored on enum properties; other enums read as their underlying integer.
- `Guid`, `DateOnly`/`TimeOnly`, and nullable types all read exactly as they do on entities.

```csharp
[InquiryAdHoc]
public sealed record SaleNote          // records work — use init properties,
{                                      // not positional parameters
    public long Id { get; init; }      // column "Id"
    public string? Note { get; init; } // column "Note" (DBNull → null)
}
```

Inquiry resolves the names once per result set. When the columns already arrive in property order, the
rows are read straight through by position. Otherwise each row reads through the resolved ordinals. The
resolved map is reused while a later result has the same column names.

## What the generator emits

For each `[InquiryAdHoc]` type: two materializers in a `{MetadataIdentity}.InquiryAdHoc.g.cs` file (namespace-qualified, with containing types separated by `+`, and generic arity backticks preserved as part of the CLR metadata identity, such as ``Outer`1+Inner`2``), plus a registration line in `AddInquiryGeneratedStores()`. The class materializer is the one that DI registers. Before the first row, the pipeline binds it to the result columns, and that returns the name-mapped struct:

```csharp
internal readonly struct CategorySalesInquiryAdHocStructMaterializer
    : IInquiryEntityMaterializer<CategorySales>,
      IInquiryResultSetBinder<CategorySales, CategorySalesInquiryAdHocStructMaterializer>
{
    private static readonly InquiryResultShape s_shape = new(
        typeof(CategorySales),
        new[] { "Category", "TotalAmount", "OrderCount" },
        new[] { "Category", "TotalAmount", "OrderCount" });

    // Resolves the property ordinals from the reader's column names.
    public CategorySalesInquiryAdHocStructMaterializer Bind(DbDataReader reader) => new(s_shape.Map(reader));

    public CategorySales Materialize(DbDataReader reader)
    {
        // Columns in property order: read ordinals 0, 1, 2 straight through.
        // Otherwise: read each property's resolved ordinal, in ascending ordinal order.
        // A failed read is rethrown with the property and column names.
    }
}
```

That DI registration is what the ad-hoc `IInquiry.Query*` path resolves at runtime — without it, querying an unregistered type throws. Registration goes through the same `AddInquiryGeneratedStores()` (or assembly-scanning `AddInquiry(Assembly[])`) call you already make; nothing extra to wire.

Generated materializers read every value in ascending ordinal order, whatever order the SELECT list
uses. So Inquiry opens their readers with `CommandBehavior.SequentialAccess`. Wide rows and large final
columns can therefore be consumed without the provider buffering the complete row first, including
when `QueryAsync<T>` is stopped early. Hand-written `IInquiryEntityMaterializer<T>` implementations
remain buffered by default, so existing materializers that read columns out of order keep working
safely. They keep their own mapping and do not need the binding step.

## Constraints

- The type must be a **concrete class** (records included) with an accessible **parameterless constructor**. A positional record (`record RegionTotal(string Region, decimal Total)`) has only its primary constructor, so it is rejected with `INQ046` — use init-only properties instead.
- A type with no mappable properties is rejected with `INQ045`.
- Two properties whose names differ only by case are rejected with `INQ106`.
- Ad-hoc DTOs are read shapes only: they generate no DDL, no mutations, and cannot be used as store entity types.

## See also

- [Projections](projections.md) — column subsets of one entity, with generated SQL.
- [Aggregates](aggregates.md) — single scalar results.
- [Security](../security.md) — how interpolated ad-hoc SQL is parameterized.
