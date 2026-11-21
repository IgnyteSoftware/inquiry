# Choosing a select declaration

## 1.0 decision

Retain the existing select attributes and named partial methods. Do not introduce an InquirySelect
rename for 1.0. Prefer explicit nameof fields over deriving query meaning from a method name.

Use InquirySelectAllByPredicate plus InquiryWhere as the canonical general filtering declaration.
Use InquirySelectAllByField for equality-only filters, especially when returning a projection.
Keep InquirySelectOneByKey as the explicit key lookup. Use InquirySelectOneByField to read one row by
a non-key field that the application treats as unique. InquirySelectAll remains the unfiltered form.
This is a documented division of capabilities, not a claim that all forms are interchangeable.

The projection restriction on predicate selects is accepted for 1.0. They return the store entity,
not an InquiryProjection DTO. To filter a projection beyond field equality, write parameterized SQL
with an explicitly mapped result, or read entities and map them in application code when fetching all
entity columns is acceptable. Do not move filtering into memory merely to bypass this restriction.

## Supported capabilities

| Declaration | Result shape | Filtering | DISTINCT / ordering | Pagination | Eager relations | Omit columns |
|---|---|---|---|---|---|---|
| InquirySelectAll | Entity or projection; buffered list or stream | No method predicates | Both | Ordered offset, buffered; optional total | No | Exclude, entity results only |
| InquirySelectAllByField | Entity or projection; buffered list or stream | Listed fields joined by equality AND | Both | Ordered offset, buffered; optional total | No | Exclude, entity results only |
| InquirySelectAllByPredicate + InquiryWhere | Entity only; buffered list or stream | Comparisons, groups, optional criteria | Both | Ordered offset, buffered; optional total | No | Exclude |
| InquirySelectOneByKey | Nullable entity | Primary key | Not configurable | None | No | Exclude |
| InquirySelectOneByField | Nullable entity or projection; a second match throws | One field by equality | Not configurable | None | No | Exclude, entity results only |
| InquiryKeysetPage | InquiryPage of the entity and cursor | Cursor seek, not arbitrary InquiryWhere filters | Key-field order and one direction; no DISTINCT option | Keyset only | No | No |
| InquirySelectAllEager / InquirySelectOneByKeyEager | Entity; buffered list or stream / nullable entity | All parents / primary key | Not configurable | None | Declared relationships, or those named by InquiryInclude | No |

For offset paging, parameters end with int offset, int limit, then CancellationToken. Supply a stable
OrderBy including a unique tie-breaker. InquiryPagedResult adds a total; the page and the COUNT
are sent as one command (see [Page with a total](pagination.md#page-with-a-total)). DISTINCT with that result
shape is rejected. A list-returning offset query may use DISTINCT, subject to the provider's SQL rules.

Keyset ordering fields must be non-nullable in the database and include a unique tie-breaker.
Eager methods are separate operations, not an Include modifier on arbitrary filtered/projection
queries. Runtime ContextKey filters in an eager tree are rejected. See
[Eager loading](eager-loading.md) and [Pagination](pagination.md) for their further constraints.

## Current declarations

These snippets use Inquiry.Stores, System.Collections.Generic, System.Threading, and
System.Threading.Tasks. Product has an integer ProductID key, nullable integer CategoryID, string
ProductName, and decimal UnitPrice. ProductSummary is an InquiryProjection of Product.

A simple entity filter uses explicit field names. Renaming FindByCategoryAsync does not change SQL:

```csharp
[InquirySelectAllByPredicate]
[InquiryWhere(nameof(Product.CategoryID))]
public partial Task<IReadOnlyList<Product>> FindByCategoryAsync(
    int? categoryId, CancellationToken ct = default);
```

For an equality-only projection, use the field form:

```csharp
[InquirySelectAllByField(nameof(Product.CategoryID))]
public partial Task<IReadOnlyList<ProductSummary>> SummariesAsync(
    int? categoryId, CancellationToken ct = default);
```

To read one row by a non-key field, use the single-row form. It returns the row, or null when no
row matches:

```csharp
[InquirySelectOneByField(nameof(Product.ProductName))]
public partial Task<Product?> GetByNameAsync(
    string productName, CancellationToken ct = default);
```

The method follows the `QuerySingleOrDefaultAsync` rule: when more than one row matches, it throws
`InvalidOperationException`, and the message names the store method and the field. Do not declare
InquirySelectAllByField and call `SingleOrDefault()` on the list. That reads every match, and a
duplicate surfaces as a generic LINQ error. The generated SQL reads at most two rows, which is enough
to detect a duplicate: `LIMIT 2`, `TOP (2)` on SQL Server, and `ROWNUM <= 2` on Oracle. Oracle uses
`ROWNUM` because it rejects `FOR UPDATE` together with `FETCH FIRST`. With `UpdateSkipLocked`, a
locked duplicate is skipped rather than reported, and Oracle rejects the combination with INQ039; see
[Row locking](row-locking.md#limitations).

The method takes one parameter of the field's type, then the CancellationToken. The return type can
also be a nullable projection. `IncludeDeleted`, `LockMode`, `Exclude`, global filters, and `InquiryIgnoreFilter`
behave as on InquirySelectAllByField. An unknown field reports INQ007, a return type other than
`Task<T?>` reports INQ005, and a parameter that does not match the field type reports INQ006. To
match on more than one field, use InquirySelectAllByPredicate. A database unique constraint on the
field is still the reliable guard against duplicates; this method only detects them on read.

An ordered offset query keeps values and paging parameters separate:

```csharp
[InquirySelectAllByPredicate(
    OrderBy = nameof(Product.ProductName) + " ASC, " + nameof(Product.ProductID) + " ASC",
    Paged = true)]
[InquiryWhere(nameof(Product.CategoryID))]
public partial Task<IReadOnlyList<Product>> PageByCategoryAsync(
    int? categoryId, int offset, int limit, CancellationToken ct = default);
```

The compound condition below means category matches AND either price is at least the minimum or
the name matches the supplied LIKE pattern:

```csharp
[InquirySelectAllByPredicate]
[InquiryWhere(nameof(Product.CategoryID))]
[InquiryWhere(nameof(Product.UnitPrice), Compare.GreaterThanOrEqual, OpenGroups = 1)]
[InquiryWhere(nameof(Product.ProductName), Compare.Like, Or = true, CloseGroups = 1)]
public partial Task<IReadOnlyList<Product>> SearchAsync(
    int? categoryId, decimal minimumPrice, string namePattern, CancellationToken ct = default);
```

Binding remains positional. The first criterion consumes categoryId, the next minimumPrice, and the
last namePattern. nameof protects mapped field names, not parameter binding. Between consumes two
parameters, IN consumes a collection, and IS NULL consumes none. Do not reorder criteria independently
of their parameters. To bind each criterion to a parameter by name instead, set `Parameter = nameof(...)`;
see [bind criteria by name](query-composition.md#bind-criteria-by-name).

## Omit expensive columns

`Exclude` reads the store entity without some of its columns, such as image or document bytes. It is
available on InquirySelectAll, InquirySelectOneByKey, InquirySelectAllByField, InquirySelectOneByField, and
InquirySelectAllByPredicate. Here Announcement has an `int Id` key, `string Title`, and a
`byte[]? Image`:

```csharp
[InquirySelectAll(
    OrderBy = nameof(Announcement.Id),
    Exclude = new[] { nameof(Announcement.Image) })]
public partial Task<IReadOnlyList<Announcement>> ListAsync(CancellationToken ct = default);

[InquirySelectOneByKey]
public partial Task<Announcement?> GetAsync(int id, CancellationToken ct = default);
```

ListAsync leaves `Image` out of the generated `SELECT` list, and its materializer does not read the
column. Each returned `Image` keeps the value the entity gives it when it is constructed, here `null`.
GetAsync has no `Exclude`, so it reads every column. The result stays the entity type, and a column
added to the entity later joins the read automatically. Name each column with `nameof`, or with its
mapped column name.

The read must keep the columns that identify the row, guard a later write, filter the active rows, or
the query needs. `INQ122` is a build error when `Exclude` names one of these:

- a key column, or the `[InquiryConcurrencyToken]` column;
- the `[InquirySoftDelete]` column, or an `[InquiryGlobalFilter]` column;
- a foreign key that an `[InquiryRelation]` or `[InquiryManyToMany]` joins on, on this entity or another;
- a column in the method's `OrderBy`;
- a C# `required` property, which the materializer must set.

It also rejects an unknown or repeated name, a name that matches more than one column, a list that
leaves a keyless view with no column, and `Exclude` on a method that returns a projection, because a
projection already reads only its own columns. A name equal to a property name always means that
property. Eager-loading methods and handwritten SQL
have no `Exclude` option.

> [!WARNING]
> Do not write an entity read with `Exclude` back with a full `[InquiryUpdate]`. That update sets
> every column from the entity, so it overwrites each omitted column with the default value the read
> left in its property. Before you update the row, read it again in full, or update only the changed
> columns with a [set-based update](set-based-mutations.md).

## All, unassigned, or one category

A search form often offers three category choices: all categories, products with no category, and
one category. One nullable argument cannot carry all three. With `Optional = true`, null disables
the criterion. Null does not mean `CategoryID IS NULL`.

Declare two methods. The first covers "all" and "one category". The second covers "unassigned":

```csharp
[InquirySelectAllByPredicate(
    OrderBy = nameof(Product.ProductName) + " ASC, " + nameof(Product.ProductID) + " ASC")]
[InquiryWhere(nameof(Product.CategoryID), Optional = true)]
public partial Task<IReadOnlyList<Product>> ByCategoryAsync(
    int? categoryId, CancellationToken ct = default);

[InquirySelectAllByPredicate(
    OrderBy = nameof(Product.ProductName) + " ASC, " + nameof(Product.ProductID) + " ASC")]
[InquiryWhere(nameof(Product.CategoryID), Compare.IsNull)]
public partial Task<IReadOnlyList<Product>> UnassignedAsync(CancellationToken ct = default);
```

Let the application own the three-state choice and select the method:

```csharp
public enum CategoryMode { Any, Unassigned, Specific }

public readonly record struct CategoryFilter(CategoryMode Mode, int CategoryId = 0)
{
    public static CategoryFilter Any => new(CategoryMode.Any);
    public static CategoryFilter Unassigned => new(CategoryMode.Unassigned);
    public static CategoryFilter EqualTo(int categoryId) => new(CategoryMode.Specific, categoryId);
}

public static class ProductSearch
{
    public static Task<IReadOnlyList<Product>> SearchAsync(
        ProductStore store, CategoryFilter filter, CancellationToken ct = default)
        => filter.Mode switch
        {
            CategoryMode.Any => store.ByCategoryAsync(null, ct),
            CategoryMode.Specific => store.ByCategoryAsync(filter.CategoryId, ct),
            CategoryMode.Unassigned => store.UnassignedAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };
}
```

Only null disables an optional criterion. `false` and `0` are values and filter for that value. An
empty string is a value on every provider except Oracle. Oracle treats an empty string as NULL, so
`""` disables an optional string criterion there. Each method keeps one constant SQL shape. Add other criteria, ordering, and paging
to both declarations in the same way. Each extra choice multiplies the declarations, so keep the
set of choices small. Inquiry has no built-in filter type for this; the
[decision record](../../develop/api-contract-decisions.md#425-absent-null-and-value-states-for-optional-filters)
explains why.

## User-selected sorting

`OrderBy` is fixed when the store compiles. To let users sort a grid, declare one method per allowed
order and select one in application code. Never place user input in SQL as an identifier.

```csharp
public enum ProductSort { Name, NameDescending, Price, PriceDescending }

public partial class ProductStore
{
    [InquirySelectAll(OrderBy = "ProductName ASC, ProductID ASC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> PageByNameAsync(
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAll(OrderBy = "ProductName DESC, ProductID DESC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> PageByNameDescendingAsync(
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAll(OrderBy = "UnitPrice ASC, ProductID ASC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> PageByPriceAsync(
        int offset, int limit, CancellationToken ct = default);

    [InquirySelectAll(OrderBy = "UnitPrice DESC, ProductID DESC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> PageByPriceDescendingAsync(
        int offset, int limit, CancellationToken ct = default);
}

public static class ProductGrid
{
    public static Task<IReadOnlyList<Product>> PageAsync(
        ProductStore store, ProductSort sort, int offset, int limit, CancellationToken ct = default)
        => sort switch
        {
            ProductSort.Name => store.PageByNameAsync(offset, limit, ct),
            ProductSort.NameDescending => store.PageByNameDescendingAsync(offset, limit, ct),
            ProductSort.Price => store.PageByPriceAsync(offset, limit, ct),
            ProductSort.PriceDescending => store.PageByPriceDescendingAsync(offset, limit, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unsupported sort order."),
        };
}
```

Parse the request value into `ProductSort` at the application boundary. `Enum.TryParse` also
accepts undefined numbers, such as `"42"`; the switch rejects them before any database call.

- **Tie-breaker.** End each order with a unique column, here `ProductID`. Without it, rows with equal
  names or prices can move between pages.
- **Null values.** `OrderBy` accepts only `ASC` and `DESC` (`INQ042` for `NULLS FIRST`). Where nulls
  sort depends on the provider. Sort on a non-nullable column, or write SQL, when null placement
  must be the same on every provider.
- **Filters.** Put the same criteria on every sort method. Sorting and the category choice above
  multiply: four orders and two category methods need eight declarations.
- **Offset paging.** When the user changes the order or a filter, start again at offset 0. An old
  offset points into a different row sequence.
- **Keyset paging.** Declare one `InquiryKeysetPage` method per order. Keyset columns must be
  non-nullable and share one direction. Keyset methods take no `InquiryWhere` criteria, so a grid
  with user filters uses offset paging. A cursor belongs to one order. When the order changes,
  start again with a null cursor.

## Comparison with a proposed unified attribute

The following is a proposal only. InquirySelect does not exist in the current packages:

```csharp
// Proposal only. Do not paste into a current consumer.
[InquirySelect]
[InquiryWhere(nameof(Product.CategoryID))]
public partial Task<IReadOnlyList<Product>> FindByCategoryAsync(
    int? categoryId, CancellationToken ct = default);
```

| Query | Current form | Proposed unified form | Decision |
|---|---|---|---|
| Simple filter | ByPredicate + Where, or ByField for equality | Select + Where | Keep explicit existing forms; no call-site change needed. |
| Compound condition | ByPredicate + grouped Where criteria | Select + the same criteria | A rename does not make grouping or binding clearer. |
| Projection | SelectAll / ByField returns a mapped DTO | Select returning a DTO with Where | Requires new projection/predicate support, not just a rename. |
| Ordering / offset | OrderBy and Paged on supported attributes | Same options on Select | Shared spelling saves little while requiring migration and diagnostics changes. |
| Keyset / eager | Separate operations with distinct results and lifetimes | Would need additional modes and restrictions | Keep their explicit declarations. |

A future replacement would need equivalent result shapes, diagnostics, a deprecation period, and a
mechanical migration before removing old forms. None is required now: existing attributes remain
supported and no obsolete aliases are introduced. Moving a field-less ByField declaration to explicit
nameof arguments is optional; preserve its field order and parameter types. This decision does not
change mutations. Table-wide deletion still requires the explicit InquiryDeleteAll or
InquiryHardDeleteAll declaration.

## When to write SQL

Use attributes for fixed queries whose complete condition and parameter order a reviewer can read
at the declaration. A single visible AND/OR group can be reasonable, as in SearchAsync above.
When nested groups, negation, or reusable specifications require tracing counters across declarations
to understand the query, prefer handwritten parameterized SQL in a named application method.

For example, keep the compound condition directly visible in SQL once it grows beyond that boundary.
Use the provider's quoting and parameterized Inquiry SQL APIs; do not concatenate user values or
user-selected identifiers. This is a readability choice, not an arbitrary limit enforced by the parser.

Neither retained nor proposed attributes provide runtime LINQ or arbitrary user-selected composition.
Use a finite set of named generated queries for known shapes, or explicit SQL for a shape that needs
joins, CTEs, subqueries, or unsupported projection predicates. Review that SQL and its materializer as
part of the application contract.

See [Projections](projections.md), [Ad-hoc DTOs](ad-hoc-dtos.md), and [CRUD](crud.md).
