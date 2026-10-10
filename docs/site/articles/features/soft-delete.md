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
    [InquiryDelete]
    public partial Task<bool> DeleteAsync(string customerID, CancellationToken ct = default);

    // Opt-out: include soft-deleted rows in this select
    [InquirySelectAll(IncludeDeleted = true)]
    public partial Task<IReadOnlyList<Customer>> SelectAllIncludingDeletedAsync(CancellationToken ct = default);

    // Opt-out: literal DELETE, bypassing the soft-delete column
    [InquiryHardDelete]
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
    "UPDATE \"Customers\" SET \"IsDeleted\" = 1 WHERE \"CustomerID\" = @CustomerID AND \"IsDeleted\" = 0";

// IncludeDeleted opt-out: filter is omitted
private const string _sqlSelectAllIncludingDeleted =
    "SELECT \"CustomerID\", \"CompanyName\", \"IsDeleted\" FROM \"Customers\"";

// HardDelete opt-out: literal DELETE
private const string _sqlHardDelete =
    "DELETE FROM \"Customers\" WHERE \"CustomerID\" = @CustomerID";
```

## Archive, edit, and restore

Ordinary updates match active rows and never assign the soft-delete marker. To restore a row,
use `[InquiryRestoreOneByKey]`; changing `IsDeleted` on an entity cannot restore it.

A single-row archive matches the key, original concurrency token (when configured), active-row
predicate, and write-enforced filters. Success advances an integral token or rotates an opted-in
string/Guid stamp. SQL Server advances database-generated rowversion itself. Repeated archives
return false/null, including an attempt with the archived row's current token. With a token and
`ThrowOnConcurrencyConflict`, a zero-row archive throws instead. The input entity remains unchanged.

`[InquiryDelete]` returning `Task<TEntity?>` returns the archived row, including its new token and
delete marker. It returns null when no row was archived. For example, after archiving Version 0,
the returned entity has Version 1; updating the old Version 0 entity returns false and leaves the row deleted
(or throws InquiryConcurrencyException when configured). Entity updates with no writable columns
after excluding the marker fail at build time with INQ096.

Restore keeps its key-only administrative contract: it does not compare an expected token.
It matches only deleted rows in the enforced filter scope, clears the marker, and advances or
rotates the token. Repeated restore returns false. Reload before editing the restored row.
Authorize this operation separately when users must not restore arbitrary records.

Table-wide and predicate deletion retain their existing set-based contract; this single-row token
contract does not add optimistic checks to those operations. Hard delete can still remove an
archived row and checks its supplied token when configured. Tokenless upserts keep their existing
conflict-update behavior for other columns, including on archived rows, but preserve the stored
soft-delete marker. Their insert branch uses the supplied marker. Use explicit restore to reactivate
an existing row; upsert is not a substitute for active-row update.

## Returning capability

| Provider | Single-row soft-delete returning | Zero-row protection |
|---|---|---|
| SQLite | Supported | Native UPDATE RETURNING |
| PostgreSQL | Supported | Native UPDATE RETURNING |
| SQL Server | Supported | OUTPUT INTO captures only affected rows |
| MySQL / MariaDB | Supported | Read-back requires ROW_COUNT() > 0 |
| Oracle | Supported | SQL%ROWCOUNT selects an empty cursor on failure |

All paths enforce the write filters on the mutation. MySQL-family and Oracle read-back use the
key only after proving the mutation affected a row. Unsupported operations outside this matrix
still report `INQ039` unless the project explicitly enables runtime stubs.

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
| Oracle | `IsDeleted = 0` (unquoted) | `NUMBER(1) NOT NULL` |

## When to soft-delete

Soft delete is right when you need:

- Audit / undo capability — "who deleted this customer and when?"
- Foreign-key preservation — child rows shouldn't break when a parent is "removed"
- GDPR right-to-erasure with a two-phase workflow — mark deleted, purge later

Soft delete is **not** right when:

- Storage is the constraint (deleted rows still occupy disk)
- True compliance erasure is mandatory (use `[InquiryHardDelete]` instead)
- Foreign keys cascade is the desired behavior

## See also

- [Global query filters](global-filters.md) — the same active-row machinery generalized to columns you define (tenancy, publish state, activation).
- [CRUD](crud.md) — the baseline operations soft-delete extends.
- [Optimistic concurrency](concurrency.md) — composes with soft delete.
