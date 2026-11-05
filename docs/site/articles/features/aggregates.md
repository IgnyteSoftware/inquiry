# Aggregates

Return a single value from your store: a row count, a `SUM`, `AVG`, `MIN` or `MAX` over one column,
or a yes/no existence test. An aggregate can take `[InquiryWhere]` criteria, so you can total one
category without loading its rows. A plain count cannot; see [Count shapes](#count-shapes).

The example on this page is a complete program. It runs against SQLite and prints the output shown
under [Run it](#run-it).

## You write

Create **Product.cs**:

<!-- recipe: aggregates/Product.cs -->
```csharp
using Inquiry.Entities;

[InquiryTable("Products")]
public sealed class Product
{
    [InquiryKey("ProductID", IsGenerated = true)]
    public int? ProductID { get; set; }

    [InquiryColumn]
    public string ProductName { get; set; } = "";

    [InquiryColumn]
    public string Category { get; set; } = "";

    [InquiryColumn]
    public decimal UnitPrice { get; set; }

    [InquiryColumn]
    public int UnitsInStock { get; set; }
}
```

Create **ProductStore.cs**:

<!-- recipe: aggregates/ProductStore.cs -->
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Stores;

public partial class ProductStore : InquiryStore<Product>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Product product, CancellationToken ct = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken ct = default);

    [InquiryGroupCount("Category")]
    public partial Task<IReadOnlyList<GroupCount<string>>> CountByCategoryAsync(
        CancellationToken ct = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "UnitsInStock")]
    public partial Task<int?> TotalStockAsync(CancellationToken ct = default);

    [InquiryAggregate(InquiryAggregateFunction.Avg, "UnitPrice")]
    public partial Task<decimal?> AverageUnitPriceAsync(CancellationToken ct = default);

    [InquiryAggregate(InquiryAggregateFunction.Sum, "UnitsInStock")]
    [InquiryWhere("Category")]
    public partial Task<int?> StockInCategoryAsync(
        string category, CancellationToken ct = default);

    [InquiryAggregate(InquiryAggregateFunction.Max, "UnitPrice")]
    [InquiryWhere("Category", Parameter = nameof(category))]
    [InquiryWhere("UnitsInStock", Compare.GreaterThan, Parameter = nameof(minimumStock))]
    public partial Task<decimal> HighestPriceInStockAsync(
        string category, int minimumStock, CancellationToken ct = default);

    [InquiryExists]
    public partial Task<bool> AnyAsync(CancellationToken ct = default);

    [InquiryExists]
    [InquiryWhere("ProductName")]
    public partial Task<bool> ExistsByNameAsync(
        string productName, CancellationToken ct = default);
}
```

`InquiryAggregate` takes an `InquiryAggregateFunction` (`Sum`, `Avg`, `Min` or `Max`) and the
mapped property or column to aggregate.

## Predicate parameters

An aggregate accepts the same `[InquiryWhere]` criteria as a predicate select, and they AND-compose
in declaration order. By default each criterion binds to the next method parameter, in order, before
the trailing `CancellationToken`. `StockInCategoryAsync` binds its one criterion to `category` that
way.

To bind by name, set `Parameter` on every value-consuming criterion, as `HighestPriceInStockAsync`
does. The full rules, including `Compare` operators, groups and optional criteria, are in
[Query composition](query-composition.md#bind-criteria-by-name).

## Nullable results

SQL returns `NULL` from `SUM`, `AVG`, `MIN` and `MAX` when no row matches. Inquiry converts `NULL` to
`default(T)` for the method's return type:

- With a nullable `T`, such as `int?` or `decimal?`, you receive `null`. Use this when "no rows" must
  stay distinct from zero.
- With a non-nullable value type, such as `decimal`, you receive `0`.

The output below shows both: `StockInCategoryAsync("Produce")` returns `null`, and
`HighestPriceInStockAsync("Produce", 20)` returns `0`.

The provider's result is converted to `T`. `SUM` or `AVG` over a floating-point column can come back
as a `double`, so converting it to `decimal` carries floating-point rounding. Use a decimal or integer
column, and a matching `T`, when you need exact results.

## Count shapes

| You want | Declare | Returns |
|---|---|---|
| Rows in the table | `[InquiryCount]` | `Task<long>` |
| Rows per value of one column | `[InquiryGroupCount("Column")]` | `Task<IReadOnlyList<GroupCount<TKey>>>` |
| Whether any row matches criteria | `[InquiryExists]` plus `[InquiryWhere]` | `Task<bool>` |
| A filtered total alongside a page of rows | `InquiryPagedResult<T>` on a predicate select | see [Choosing a select declaration](select-declarations.md#supported-capabilities) |

`[InquiryCount]` must return `Task<long>` and take only a `CancellationToken`. It counts the whole
table, minus rows hidden by [soft delete](soft-delete.md) or [global filters](global-filters.md).
Do not add `[InquiryWhere]` to it. A criterion that takes a value fails with
[INQ006](../diagnostics.md#inq006). A criterion that takes no value, such as `Compare.IsNull`, is
currently ignored, and the method still counts the whole table.

`[InquiryGroupCount]` returns one `GroupCount<TKey>` per distinct value, with `Key` and `Count`. SQL
does not guarantee the order of the groups, so sort them in your code when order matters.

## The generator emits

Each method gets its own SQL constant. For the store above on SQLite:

```csharp
private const string _sqlCount = "SELECT COUNT(*) FROM \"Products\"";
private const string _sqlAgg_TotalStockAsync = "SELECT SUM(\"UnitsInStock\") FROM \"Products\"";
private const string _sqlAgg_StockInCategoryAsync = "SELECT SUM(\"UnitsInStock\") FROM \"Products\" WHERE \"Category\" = @Category";
private const string _sqlAgg_HighestPriceInStockAsync = "SELECT MAX(\"UnitPrice\") FROM \"Products\" WHERE \"Category\" = @Category AND \"UnitsInStock\" > @UnitsInStock";
```

SQL parameter names come from the columns, not the method parameters, so `minimumStock` binds to
`@UnitsInStock`. Each aggregate runs as a scalar query, and the return type drives the conversion.

## Existence checks

`[InquiryExists]` returns `Task<bool>`, the `EXISTS` analog of EF Core's `.AnyAsync()`. The database
can stop at the first match, so it is cheaper than `COUNT(*) > 0`. Apply zero or more `[InquiryWhere]`
criteria, as on a predicate select. With none, as on `AnyAsync`, it tests whether the table has any
row.

```csharp
private const string _sqlExists_AnyAsync =
    "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"Products\") THEN 1 ELSE 0 END";
private const string _sqlExists_ExistsByNameAsync =
    "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"Products\" WHERE \"ProductName\" = @ProductName) THEN 1 ELSE 0 END";
```

The `CASE WHEN EXISTS(…) THEN 1 ELSE 0 END` form is portable across SQLite, SQL Server, PostgreSQL and
MySQL; Oracle appends `FROM DUAL`. The `1` or `0` result is converted to `bool`. Like the aggregates,
the inner test applies the active-row filter, so a [soft-deleted](soft-delete.md) or
[globally filtered](global-filters.md) row does not count as existing.

## Run it

The project setup matches [Getting started](../getting-started.md#create-the-project). Replace
**Program.cs**:

<!-- recipe: aggregates/Program.cs -->
```csharp
using System;
using System.Linq;
using System.Threading;
using Inquiry.DependencyInjection;
using Inquiry.Generated;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

const string connectionString = "Data Source=inquiry-aggregates.db";
var ct = CancellationToken.None;

await using (var connection = new SqliteConnection(connectionString))
{
    await connection.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = InquiryGeneratedSchema.Ddl;
    await command.ExecuteNonQueryAsync(ct);
}

var services = new ServiceCollection();
services.AddInquiry();
services.AddInquiryGeneratedStores();
services.AddInquirySqlite(connectionString);

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var products = scope.ServiceProvider.GetRequiredService<ProductStore>();

await products.InsertAsync(new Product
    { ProductName = "Chai", Category = "Beverages", UnitPrice = 18m, UnitsInStock = 39 }, ct);
await products.InsertAsync(new Product
    { ProductName = "Chang", Category = "Beverages", UnitPrice = 19m, UnitsInStock = 17 }, ct);
await products.InsertAsync(new Product
    { ProductName = "Aniseed Syrup", Category = "Condiments", UnitPrice = 23m, UnitsInStock = 13 }, ct);

Console.WriteLine($"Products: {await products.CountAsync(ct)}");
Console.WriteLine($"Units in stock: {await products.TotalStockAsync(ct)}");
Console.WriteLine($"Average unit price: {await products.AverageUnitPriceAsync(ct)}");
Console.WriteLine($"Units in stock, Beverages: {await products.StockInCategoryAsync("Beverages", ct)}");

var produceStock = await products.StockInCategoryAsync("Produce", ct);
Console.WriteLine($"Units in stock, Produce: {produceStock?.ToString() ?? "none"}");

Console.WriteLine("Highest price, Beverages with more than 20 in stock: "
    + await products.HighestPriceInStockAsync("Beverages", 20, ct));
Console.WriteLine("Highest price, Produce with more than 20 in stock: "
    + await products.HighestPriceInStockAsync("Produce", 20, ct));

var groups = await products.CountByCategoryAsync(ct);
foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"Products in {group.Key}: {group.Count}");
}

Console.WriteLine($"Any products: {await products.AnyAsync(ct)}");
Console.WriteLine($"Chai exists: {await products.ExistsByNameAsync("Chai", ct)}");
Console.WriteLine($"Tofu exists: {await products.ExistsByNameAsync("Tofu", ct)}");
```

On a new database the program prints:

<!-- recipe: aggregates/output.txt -->
```text
Products: 3
Units in stock: 69
Average unit price: 20
Units in stock, Beverages: 56
Units in stock, Produce: none
Highest price, Beverages with more than 20 in stock: 18
Highest price, Produce with more than 20 in stock: 0
Products in Beverages: 2
Products in Condiments: 1
Any products: True
Chai exists: True
Tofu exists: False
```

The Beverages total, 56, excludes the Condiments row that the unfiltered total of 69 includes. The
highest Beverages price with more than 20 in stock is 18, because Chang has only 17 in stock.

As in Getting started, the generated DDL creates the schema once. Delete `inquiry-aggregates.db` before
running the program again.

## See also

- [Query composition](query-composition.md): `[InquiryWhere]` operators, groups and named binding.
- [CRUD](crud.md): the other generated store operations.
