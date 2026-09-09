# Choosing a select declaration

## 1.0 decision

Retain the existing select attributes and named partial methods. Do not introduce an InquirySelect
rename for 1.0. Prefer explicit nameof fields over deriving query meaning from a method name.

Use InquirySelectAllByPredicate plus InquiryWhere as the canonical general filtering declaration.
Use InquirySelectAllByField for equality-only filters, especially when returning a projection.
Keep InquirySelectOneByKey as the explicit key lookup. InquirySelectAll remains the unfiltered form.
This is a documented division of capabilities, not a claim that all forms are interchangeable.

The projection restriction on predicate selects is accepted for 1.0. They return the store entity,
not an InquiryProjection DTO. To filter a projection beyond field equality, write parameterized SQL
with an explicitly mapped result, or read entities and map them in application code when fetching all
entity columns is acceptable. Do not move filtering into memory merely to bypass this restriction.

## Supported capabilities

| Declaration | Result shape | Filtering | DISTINCT / ordering | Pagination | Eager relations |
|---|---|---|---|---|---|
| InquirySelectAll | Entity or projection; buffered list or stream | No method predicates | Both | Ordered offset, buffered; optional total | No |
| InquirySelectAllByField | Entity or projection; buffered list or stream | Listed fields joined by equality AND | Both | Ordered offset, buffered; optional total | No |
| InquirySelectAllByPredicate + InquiryWhere | Entity only; buffered list or stream | Comparisons, groups, optional criteria | Both | Ordered offset, buffered; optional total | No |
| InquirySelectOneByKey | Nullable entity | Primary key | Not configurable | None | No |
| InquiryKeysetPage | InquiryPage of the entity and cursor | Cursor seek, not arbitrary InquiryWhere filters | Key-field order and one direction; no DISTINCT option | Keyset only | No |
| InquirySelectAllEager / InquirySelectOneByKeyEager | Entity stream / nullable entity | All parents / primary key | Not configurable | None | Declared relationships |

For offset paging, parameters end with int offset, int limit, then CancellationToken. Supply a stable
OrderBy including a unique tie-breaker. InquiryPagedResult adds a total; DISTINCT with that result
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
of their parameters. Explicit parameter binding is separate work, not a feature of this convention.

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
