# Soft delete

Mark a boolean column with `[InquirySoftDelete]` and the generator does two things:

1. **Every `SELECT` automatically composes the active-row filter** — the SQL grows a `WHERE IsDeleted = 0` clause (or the dialect's equivalent).
2. **`DeleteByKey` becomes a soft delete** — it emits `UPDATE … SET IsDeleted = 1 WHERE …` instead of a literal `DELETE`.

Both behaviors can be overridden per-method when you need to bypass them (admin tools, GDPR purges, etc.).

## You write

```csharp
using Inquiry.Entities;

[InquiryTable("Customers")]
public sealed class Customer
{
    [InquiryKey] public string CustomerID { get; set; } = "";
    [InquiryColumn] public string CompanyName { get; set; } = "";

    [InquirySoftDelete]
    [InquiryColumn]
    public bool IsDeleted { get; set; }
}

public partial class CustomerStore : InquiryStore<Customer>
{
    // Auto-composes WHERE IsDeleted = 0
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Customer>> SelectAllAsync(CancellationToken ct = default);

    // Same — IsDeleted filter AND-composed with the predicate
    [InquirySelectAllByField("CompanyName")]
    public partial Task<IReadOnlyList<Customer>> SelectByCompanyAsync(string companyName, CancellationToken ct = default);

    // Soft delete: emits UPDATE … SET IsDeleted = 1, not DELETE
    [InquiryDeleteOneByKey]
    public partial Task<bool> DeleteAsync(string customerID, CancellationToken ct = default);

    // Opt-out: include soft-deleted rows in this select
    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<Customer>> SelectAllIncludingDeletedAsync(CancellationToken ct = default);

    // Opt-out: literal DELETE, bypassing the soft-delete column
    [InquiryDeleteOneByKey(HardDelete = true)]
    public partial Task<bool> HardDeleteAsync(string customerID, CancellationToken ct = default);
}
```

## The generator emits

The active-filter literal is per-dialect — SQL Server / SQLite use `0`/`1`, PostgreSQL uses `FALSE`/`TRUE`, Oracle uses `0`/`1` over a `NUMBER(1)` column.

```csharp
// Default selects: filter is AND-composed
private const string _sqlSelectAll =
    "SELECT \"CustomerID\", \"CompanyName\", \"IsDeleted\" FROM \"Customers\" WHERE \"IsDeleted\" = 0";

private const string _sqlSelectByCompany =
    "SELECT \"CustomerID\", \"CompanyName\", \"IsDeleted\" FROM \"Customers\" " +
    "WHERE \"CompanyName\" = @CompanyName AND \"IsDeleted\" = 0";

// Soft delete (default)
private const string _sqlDelete =
    "UPDATE \"Customers\" SET \"IsDeleted\" = 1 WHERE \"CustomerID\" = @CustomerID";

// IncludeDeleted opt-out: filter is omitted
private const string _sqlSelectAllIncludingDeleted =
    "SELECT \"CustomerID\", \"CompanyName\", \"IsDeleted\" FROM \"Customers\"";

// HardDelete opt-out: literal DELETE
private const string _sqlHardDelete =
    "DELETE FROM \"Customers\" WHERE \"CustomerID\" = @CustomerID";
```

## Composition with other features

Soft delete composes correctly with everything else the generator emits:

- **Pagination.** The filter is AND-composed *before* `ORDER BY` / `LIMIT`: `WHERE <predicate> AND IsDeleted = 0 ORDER BY … LIMIT …`.
- **Keyset paging.** The filter is appended after the keyset cursor predicate.
- **Aggregates.** `SELECT COUNT(*) FROM … WHERE IsDeleted = 0`.
- **Projections.** A [projection](projections.md) over a soft-delete entity composes the filter too — `SELECT <subset> FROM … WHERE IsDeleted = 0` — even though the projected column subset doesn't include the indicator. `IncludeDeleted = true` opts out, exactly like a full-entity select.
- **Optimistic concurrency.** Update / delete still check the row-version column in addition to the soft-delete filter.

## Per-dialect literal

| Dialect | Active-row literal | Column type |
|---|---|---|
| Sqlite | `IsDeleted = 0` | `INTEGER NOT NULL` |
| SQL Server | `[IsDeleted] = 0` | `BIT NOT NULL` |
| PostgreSQL | `"IsDeleted" = FALSE` | `BOOLEAN NOT NULL` |
| MySQL | `` `IsDeleted` = 0 `` | `TINYINT(1) NOT NULL` |
| Oracle | `"ISDELETED" = 0` | `NUMBER(1) NOT NULL` |

## When to soft-delete

Soft delete is right when you need:

- Audit / undo capability — "who deleted this customer and when?"
- Foreign-key preservation — child rows shouldn't break when a parent is "removed"
- GDPR right-to-erasure with a two-phase workflow — mark deleted, purge later

Soft delete is **not** right when:

- Storage is the constraint (deleted rows still occupy disk)
- True compliance erasure is mandatory (use `HardDelete = true` instead)
- Foreign keys cascade is the desired behavior

## See also

- [Global query filters](global-filters.md) — the same active-row machinery generalized to columns you define (tenancy, publish state, activation).
- [CRUD](crud.md) — the baseline operations soft-delete extends.
- [Optimistic concurrency](concurrency.md) — composes with soft delete.
