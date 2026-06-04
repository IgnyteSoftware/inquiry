# Pagination

Inquiry supports two paging modes: **offset pagination** (`LIMIT/OFFSET`) and **keyset pagination** (also called cursor or seek paging). Both are opt-in via attribute named arguments.

## Offset pagination

Add `Paged = true` to any `[InquirySelectAll]` or `[InquirySelectAllByField]` method, plus `OrderBy = "..."` for deterministic ordering. The method picks up two extra parameters: `int offset, int limit`.

### You write

```csharp
public partial class ProductStore : InquiryStore<Product>
{
    [InquirySelectAll(OrderBy = "ProductName ASC", Paged = true)]
    public partial Task<IReadOnlyList<Product>> SelectPagedAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
```

### The generator emits

```csharp
private const string _sqlSelectAllPaged = "SELECT \"ProductID\", \"ProductName\", ... FROM \"Products\" ORDER BY \"ProductName\" ASC LIMIT @__limit OFFSET @__offset";

public partial Task<IReadOnlyList<Product>> SelectPagedAsync(int offset, int limit, CancellationToken cancellationToken)
    => Inquiry.QueryListAsync<Product, (int, int), ProductInquiryEntityStructMaterializer>(
        _sqlSelectAllPaged,
        (offset, limit),
        static (_cmd, _args) =>
        {
            var _p0 = _cmd.CreateParameter();
            _p0.ParameterName = "@__offset";
            _p0.DbType = global::System.Data.DbType.Int32;
            _p0.Value = _args.Item1;
            _cmd.Parameters.Add(_p0);
            var _p1 = _cmd.CreateParameter();
            _p1.ParameterName = "@__limit";
            _p1.DbType = global::System.Data.DbType.Int32;
            _p1.Value = _args.Item2;
            _cmd.Parameters.Add(_p1);
        },
        default,
        cancellationToken);
```

### Per-dialect SQL

| Dialect | Paging clause |
|---|---|
| Sqlite / PostgreSQL / MySQL | `LIMIT @__limit OFFSET @__offset` |
| SQL Server | `OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY` |
| Oracle | `OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY` (12c+) |

Generated offset methods validate their arguments before touching the database: `offset` must be `>= 0` and `limit` must be `> 0`, otherwise the method throws `ArgumentOutOfRangeException`.

## Keyset pagination

Offset paging gets slower as you scroll deeper — the database has to skip `N` rows. Keyset paging is `O(log n)` regardless of page depth: you remember the last row's sort key(s) and ask for *what's after them*.

Use `[InquiryKeysetPage("KeyColumn1", "KeyColumn2", …)]`. The method takes a single **nullable cursor** parameter — the previous page's last key, a value for one key column or a tuple for several — plus `int pageSize`, and returns `InquiryPage<TEntity, TCursor>` (the page items, the next cursor, and `HasMore`). Pass `null` for the first page.

### You write

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    [InquiryKeysetPage("OrderDate", "OrderID")]
    public partial Task<InquiryPage<Order, (DateTime, int)>> NextPageAsync(
        (DateTime, int)? after,
        int pageSize,
        CancellationToken cancellationToken = default);
}
```

The keyset is `(OrderDate, OrderID)` in significance order — `OrderID` is the tiebreaker for orders on the same date. Including a unique column in the keyset is required for stable paging.

### The generator emits

Keyset paging is emitted as **two** baked queries on purpose — a predicate-free first-page query and a sargable *seek* query:

```csharp
// First page (cursor is null): no WHERE, just ORDER BY + limit.
private const string _sqlNextPage_first =
    "SELECT \"OrderID\", \"CustomerID\", \"OrderDate\", ... FROM \"Orders\" " +
    "ORDER BY \"OrderDate\" ASC, \"OrderID\" ASC LIMIT @__pageSize";

// Seek (cursor supplied): a plain, sargable comparison so the engine does an index seek.
private const string _sqlNextPage =
    "SELECT \"OrderID\", \"CustomerID\", \"OrderDate\", ... FROM \"Orders\" " +
    "WHERE (\"OrderDate\" > @__cursor0) " +
    "   OR (\"OrderDate\" = @__cursor0 AND \"OrderID\" > @__cursor1) " +
    "ORDER BY \"OrderDate\" ASC, \"OrderID\" ASC LIMIT @__pageSize";

public partial async Task<InquiryPage<Order, (DateTime, int)>> NextPageAsync(
    (DateTime, int)? after, int pageSize, CancellationToken cancellationToken)
{
    if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be > 0.");
    if (pageSize == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be less than int.MaxValue.");
    // Runs the first-page query when `after` is null, the seek query otherwise (binding the cursor
    // only on the seek path). Over-fetches pageSize + 1 rows to compute HasMore and the next cursor.
    // ...
}
```

The two-query split matters for performance: the seek query uses a plain `key > @cursor` predicate so the engine can use an index seek, whereas a single `(@cursor IS NULL OR key > @cursor)` form is non-sargable and forces a full scan. For a multi-column keyset the seek predicate is the cascading `(a > @a) OR (a = @a AND b > @b)` comparison.

### Walking backward

Use `Direction = KeysetDirection.Backward` to walk in descending order:

```csharp
[InquiryKeysetPage("OrderDate", "OrderID", Direction = KeysetDirection.Backward)]
public partial Task<InquiryPage<Order, (DateTime, int)>> PreviousPageAsync(
    (DateTime, int)? after, int pageSize, CancellationToken cancellationToken = default);
```

The generator flips the comparator and the ORDER BY: `WHERE … < … ORDER BY … DESC`. The result is naturally in descending order — reverse it client-side if you want oldest-first display.

## When to use which

| Use offset paging when… | Use keyset paging when… |
|---|---|
| Showing "page 5 of 27" with a page-number UI | Infinite scroll, "next 50" / "previous 50" buttons |
| Result set is small (a few hundred rows total) | Result set is large (10k+) or grows over time |
| Skipping is acceptable performance | You need consistent latency on every page |
| Random page jumps are common | Forward/backward traversal is the access pattern |

Keyset is dramatically faster on large tables because the database can use an index seek to find the starting row instead of counting through `OFFSET` rows.

## See also

- [CRUD](crud.md) — the baseline `[InquirySelectAll]` without paging.
- [Projections](projections.md) — return a column-subset DTO instead of the full entity.
