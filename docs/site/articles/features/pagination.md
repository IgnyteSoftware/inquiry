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

## Keyset pagination

Offset paging gets slower as you scroll deeper — the database has to skip `N` rows. Keyset paging is `O(log n)` regardless of page depth: you remember the last row's sort key(s) and ask for *what's after them*.

Use `[InquiryKeysetPage("KeyColumn1", "KeyColumn2", …)]`. The method picks up one parameter per key column (the **cursor** values from the previous page's last row) plus `int limit`.

### You write

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    [InquiryKeysetPage("OrderDate", "OrderID")]
    public partial Task<IReadOnlyList<Order>> NextPageAsync(
        DateTime cursorOrderDate,
        int cursorOrderID,
        int limit,
        CancellationToken cancellationToken = default);
}
```

The keyset is `(OrderDate, OrderID)` in significance order — `OrderID` is the tiebreaker for orders on the same date. Including a unique column in the keyset is required for stable paging.

### The generator emits

```csharp
private const string _sqlNextPage =
    "SELECT \"OrderID\", \"CustomerID\", \"EmployeeID\", \"OrderDate\", ... " +
    "FROM \"Orders\" " +
    "WHERE (\"OrderDate\" > @__cursorOrderDate) " +
    "   OR (\"OrderDate\" = @__cursorOrderDate AND \"OrderID\" > @__cursorOrderID) " +
    "ORDER BY \"OrderDate\" ASC, \"OrderID\" ASC " +
    "LIMIT @__limit";

public partial Task<IReadOnlyList<Order>> NextPageAsync(
    DateTime cursorOrderDate, int cursorOrderID, int limit, CancellationToken cancellationToken)
    => Inquiry.QueryListAsync<Order, (DateTime, int, int), OrderInquiryEntityStructMaterializer>(
        _sqlNextPage,
        (cursorOrderDate, cursorOrderID, limit),
        static (_cmd, _args) => { /* bind three params */ },
        default,
        cancellationToken);
```

The generator emits the cascading `(a > @a) OR (a = @a AND b > @b)` predicate — correct keyset paging across multiple sort columns.

### Walking backward

Use `Direction = KeysetDirection.Backward` to walk in descending order:

```csharp
[InquiryKeysetPage("OrderDate", "OrderID", Direction = KeysetDirection.Backward)]
public partial Task<IReadOnlyList<Order>> PreviousPageAsync(
    DateTime cursorOrderDate, int cursorOrderID, int limit, CancellationToken cancellationToken = default);
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
