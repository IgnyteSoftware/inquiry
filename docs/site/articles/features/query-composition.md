# Query composition and expression updates

Start with one `[InquiryWhere]` per condition. Use `nameof` for mapped properties so renames remain
visible to the compiler. Add grouping or a reusable specification when it makes the query easier to
read. A one-off condition does not need its own attribute class.

The examples below were compiled and executed on SQLite. Other providers quote identifiers and
bind parameters using their own syntax; raw SQL must use the selected provider's syntax.

## Example model

```csharp
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public string Category { get; set; } = "";
    [InquiryColumn] public string Name { get; set; } = "";
    [InquiryColumn] public bool Discontinued { get; set; }
    [InquiryColumn] public int UnitsInStock { get; set; }
    [InquiryColumn, InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
    public long TenantId { get; set; }
    [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
}
```

Establish the authorized tenant scope around generated calls:

```csharp
using var scope = InquiryFilterContext.BeginScope(
    new Dictionary<string, object> { ["TenantId"] = tenantId });
```

## Groups, negation, and optional criteria

```csharp
public partial class ProductStore : InquiryStore<Product>
{
    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Product.Discontinued), OpenGroups = 1, Not = true)]
    [InquiryWhere(nameof(Product.UnitsInStock), Compare.LessThan, Or = true, CloseGroups = 1)]
    [InquiryWhere(nameof(Product.Category), Optional = true)]
    public partial Task<IReadOnlyList<Product>> AvailableAsync(
        bool discontinued, int threshold, string? category, CancellationToken ct = default);
}
```

SQLite emits:

```sql
SELECT "Id", "Category", "Name", "Discontinued", "UnitsInStock", "TenantId", "IsDeleted"
FROM "Products"
WHERE (NOT ("Discontinued" = @Discontinued OR "UnitsInStock" < @UnitsInStock)
       AND (@Category IS NULL OR "Category" = @Category))
  AND "IsDeleted" = 0 AND "TenantId" = @__gf_TenantId
```

The first criterion consumes `discontinued`, the second consumes `threshold`, and the third consumes
`category`. `AvailableAsync(true, 5, null)` keeps products that are not discontinued and have at least
5 units; null disables only the category criterion. The tenant and soft-delete predicates still apply.

`OpenGroups` and `CloseGroups` count parentheses. `Not` negates the criterion, or the group opened
by that criterion. An optional null criterion evaluates true before enclosing group negation; it
does not remove an entire negated group. Unbalanced groups fail with `INQ097`. Optional criteria
require one nullable scalar parameter and do not support `In`/`NotIn` (`INQ098`).

## Reusable plus local criteria

```csharp
[InquirySpecification]
[InquiryWhere(nameof(Product.Category))]
[AttributeUsage(AttributeTargets.Method)]
public sealed class InCategoryAttribute : Attribute { }

public partial class ProductStore
{
    [InquirySelectAllByPredicate, InCategory]
    [InquiryWhere(nameof(Product.Name), Optional = true)]
    public partial Task<IReadOnlyList<Product>> InCategoryAsync(
        string category, string? name, CancellationToken ct = default);
}
```

The specification expands at its use site, followed by the local criterion:

```sql
SELECT "Id", "Category", "Name", "Discontinued", "UnitsInStock", "TenantId", "IsDeleted"
FROM "Products"
WHERE ("Category" = @Category AND (@Name IS NULL OR "Name" = @Name))
  AND "IsDeleted" = 0 AND "TenantId" = @__gf_TenantId
```

Predicate parameters bind by position, not by their C# names. Swapping `category` and `name` in the
method declaration can still compile because both are strings, but it reverses what the predicates
receive. Keep the declaration order aligned with expanded criteria and inspect generated SQL when
reordering attributes or parameters. A specification does not establish name-based binding.

Scalar comparisons consume one argument, Between/NotBetween consume two (lower then upper),
In/NotIn consume one collection, and IsNull/IsNotNull consume none. Cancellation and paging
parameters belong after predicate arguments. Specifications reuse criteria; they do not supply
argument values or create a runtime query builder.

## Expression assignments

```csharp
public partial class ProductStore
{
    [InquiryUpdate]
    [InquirySet(nameof(Product.UnitsInStock), "{UnitsInStock} + @amount")]
    [InquiryWhere(nameof(Product.Id))]
    public partial Task<int> AddStockAsync(int amount, long id, CancellationToken ct = default);
}
```

```sql
UPDATE "Products" SET "UnitsInStock" = "UnitsInStock" + @amount
WHERE "Id" = @Id AND "IsDeleted" = 0 AND "TenantId" = @__gf_TenantId
```

`{UnitsInStock}` is a mapped column placeholder. `@amount` explicitly names the leading SET
parameter; the trailing `id` still binds the WHERE criterion positionally. Here, an initial stock
of 8 becomes 11 after `AddStockAsync(3, id)` and the affected-row count is 1.

Expressions are attribute constants, not user input. Assignment parameters must match the target
column type. Missing parameters, invalid expressions, and conflicting assignment bindings produce
`INQ099`. Keys, generated columns, concurrency tokens, soft-delete markers, and protected filter
columns are not valid SET targets (`INQ044`). Predicate mutations on managed concurrency-token
entities remain rejected (`INQ022`); see [set-based mutations](set-based-mutations.md).

## When SQL is clearer

For a grouped report with HAVING and explicit policy conditions, handwritten SQL can be easier to
review than a growing attribute declaration. This complete SQLite example uses the same table:

```csharp
[InquiryAdHoc]
public sealed class StockReport
{
    public string Category { get; init; } = "";
    public long Units { get; init; }
    public long ProductCount { get; init; }
}

public static class StockReports
{
    public static Task<IReadOnlyList<StockReport>> ReadAsync(
        IInquiry inquiry, long tenantId, int minimumStock, long minimumTotal,
        CancellationToken ct = default)
        => inquiry.QueryListAsync<StockReport>($"""
            SELECT Category, SUM(UnitsInStock) AS Units, COUNT(*) AS ProductCount
            FROM Products
            WHERE TenantId = {tenantId} AND IsDeleted = 0 AND Discontinued = 0
              AND UnitsInStock >= {minimumStock}
            GROUP BY Category
            HAVING SUM(UnitsInStock) >= {minimumTotal}
            ORDER BY Category
            """, ct);
}
```

Register this assembly with `AddInquiry(typeof(StockReport).Assembly)` and configure the SQLite
provider as usual. Each interpolated value becomes a bound parameter. Pass the interpolated
expression directly to `IInquiry`; do not first convert it to a string. Keep identifiers and SQL
structure fixed rather than interpolating caller-controlled SQL fragments.

The materializer matches each property to the result column with the same name, without regard to
case. The aliases give the aggregates those names. Column order does not matter, and extra columns
are ignored. A missing column throws before the first row is returned. These SQLite aggregate values
map to `long`; check provider aggregate types when porting the report. The executed example returned `Tools / 11 / 1` after the stock update,
excluding an archived product and another tenant's product.

Raw SQL does not inherit generated global filters, ambient tenant predicates, or soft-delete
policy. This author owns the explicit TenantId/IsDeleted conditions and must pass an authorized
tenant. For full entity reads, prefer supported generated queries and column-list composition over
`SELECT *`; custom reports should alias each result column to its property name.
