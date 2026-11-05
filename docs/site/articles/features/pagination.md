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
| Sqlite / PostgreSQL / MySQL / MariaDB | `LIMIT @__limit OFFSET @__offset` |
| SQL Server | `OFFSET @__offset ROWS FETCH NEXT @__limit ROWS ONLY` |
| Oracle | `OFFSET :__offset ROWS FETCH NEXT :__limit ROWS ONLY` (12c+; the generator encodes the bind names) |

Generated offset methods validate their arguments before touching the database: `offset` must be `>= 0` and `limit` must be `> 0`, otherwise the method throws `ArgumentOutOfRangeException`.

If the entity maps a property named `__offset` or `__limit`, ignoring case, the generator adds leading
underscores to that paging parameter until the name is free, for example `@___offset`. This applies to every
offset-paged method on the entity. A criterion or JSON-path value whose name equals a paging parameter binds
under a numbered name instead, such as `@__limit2`. Hover text and logged SQL show the generated names.

## Page with a total

Return `Task<InquiryPagedResult<T>>` instead of a list to get the page and the number of rows that match the query, for "page 3 of 12" displays. The return type alone turns on offset paging, so `Paged = true` is optional. The method still needs `OrderBy` (otherwise INQ020) and the trailing `int offset, int limit` parameters.

```csharp
public partial class ProductStore : InquiryStore<Product>
{
    [InquirySelectAllByPredicate(OrderBy = "ProductID ASC", Paged = true)]
    [InquiryWhere("Discontinued")]
    public partial Task<InquiryPagedResult<Product>> PageByDiscontinuedAsync(
        bool discontinued,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}

var page = await products.PageByDiscontinuedAsync(false, offset: 20, limit: 10);
IReadOnlyList<Product> rows = page.Items;   // at most 10 rows, in OrderBy order
long total = page.TotalCount;               // all matching rows, independent of offset and limit
```

`Items` holds the requested page. `TotalCount` counts every row that matches the WHERE clause, including soft-delete and global filters. A page past the end returns no items and the full total. An empty table returns no items and a total of 0.

### Supported declarations

| Declaration | Result type |
|---|---|
| `[InquirySelectAll]` | The entity or a projection |
| `[InquirySelectAllByField]` | The entity or a projection |
| `[InquirySelectAllByPredicate]` with `[InquiryWhere]` criteria | The store entity |

`Distinct = true` cannot be combined with `InquiryPagedResult` (INQ083). Keyset paging returns `InquiryPage<TEntity, TCursor>` instead and has no total.

### One command, two statements

The generator bakes a page `SELECT` and a `COUNT` with the same WHERE clause, and sends both in one command that returns two result sets:

```csharp
private const string _sql_PageByDiscontinuedAsync = "SELECT \"ProductID\", ... FROM \"Products\" WHERE \"Discontinued\" = @Discontinued ORDER BY \"ProductID\" ASC LIMIT @__limit OFFSET @__offset";
private const string _sqlCount_PageByDiscontinuedAsync = "SELECT COUNT(*) FROM \"Products\" WHERE \"Discontinued\" = @Discontinued";

public async partial Task<InquiryPagedResult<Product>> PageByDiscontinuedAsync(bool discontinued, int offset, int limit, CancellationToken cancellationToken)
{
    // argument checks ...
    var _sql = _sql_PageByDiscontinuedAsync + ";" + _sqlCount_PageByDiscontinuedAsync;
    var _cmd = new InquiryGeneratedCommand<(bool Arg0, int Arg1, int Arg2)>(
        _sql,
        (Arg0: discontinued, Arg1: offset, Arg2: limit),
        static (DbCommand _c, (bool Arg0, int Arg1, int Arg2) _args) =>
        {
            // binds @Discontinued, @__offset and @__limit once; the COUNT reuses @Discontinued
        });
    await using var _grid = await Inquiry.QueryMultipleAsync<(bool Arg0, int Arg1, int Arg2)>(_cmd, cancellationToken).ConfigureAwait(false);
    var _items = await _grid.ReadListAsync<Product, ProductInquiryEntityStructMaterializer>(default, cancellationToken).ConfigureAwait(false);
    var _total = await _grid.ReadScalarAsync<long>(cancellationToken).ConfigureAwait(false);
    return new InquiryPagedResult<Product>(_items, _total);
}
```

| Dialect | Command text |
|---|---|
| Sqlite / PostgreSQL / MySQL / MariaDB / SQL Server | `<page SELECT>;<COUNT>` (SQL Server counts with `COUNT_BIG(*)`) |
| Oracle | `DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR <page SELECT>; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR <COUNT>; DBMS_SQL.RETURN_RESULT(c); END;` |

What this costs and guarantees:

- **One round trip and one connection.** The method takes one connection from the pool, or uses the connection of the active Inquiry transaction. Interceptors see one command, and telemetry records one `QUERY_MULTIPLE` operation.
- **An extra statement.** The `COUNT` runs on every call and counts every matching row. On a large filtered set it can cost more than the page itself. If you do not need the total, return a list, or use keyset paging.
- **No snapshot by default.** The page and the `COUNT` are two statements. Outside a snapshot or serializable transaction, a write that commits between them can make `TotalCount` disagree with `Items`. For a consistent pair, call the method inside a transaction at one of those isolation levels:

```csharp
await using var transaction = await inquiry.BeginTransactionAsync(IsolationLevel.Serializable);
var page = await products.PageByDiscontinuedAsync(false, 0, 10);
await transaction.CommitAsync();
```

Inquiry does not start that transaction for you. Snapshot isolation is `IsolationLevel.Snapshot` on SQL Server (with `ALLOW_SNAPSHOT_ISOLATION` on) and is what `RepeatableRead` gives on PostgreSQL; check your database's documentation for the levels it supports.

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
// The portable dialects (SQLite/PostgreSQL/MySQL) emit a row-value comparison:
private const string _sqlNextPage =
    "SELECT \"OrderID\", \"CustomerID\", \"OrderDate\", ... FROM \"Orders\" " +
    "WHERE (\"OrderDate\", \"OrderID\") > (@__cursor0, @__cursor1) " +
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

The two-query split matters for performance: the seek query uses a plain `key > @cursor` predicate so the engine can use an index seek, whereas a single `(@cursor IS NULL OR key > @cursor)` form is non-sargable and forces a full scan. For a multi-column keyset the portable dialects (SQLite, PostgreSQL, MySQL) emit a row-value comparison `(a, b) > (@a, @b)`; SQL Server and Oracle, which lack row-value `>`, render the equivalent cascading `(a > @a) OR (a = @a AND b > @b)` form instead.

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
