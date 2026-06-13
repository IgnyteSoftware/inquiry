# Global query filters

Mark a boolean column with `[InquiryGlobalFilter]` and **every generated `SELECT` auto-composes a keep condition** on it — `WHERE IsActive = 1` (or the dialect's equivalent) — so rows that don't match are invisible to reads without any method restating the predicate. This is the [EF Core `HasQueryFilter`](https://learn.microsoft.com/ef/core/querying/filters) parity for a static column predicate, and the generalization of the [soft-delete](soft-delete.md) active-row filter to columns *you* define.

It's the common shape behind:

- **Multi-tenant isolation** — a `TenantActive` / `IsCurrentTenant` flag that hides other tenants' rows.
- **Active-record filtering** — an `IsActive` flag that hides deactivated rows by default.
- **Publish gates** — an `IsPublished` flag that hides drafts from public reads.

The condition is baked into the generated `const` SQL at compile time, so it costs nothing at runtime — exactly the same query you'd hand-write, minus the chance of forgetting it on one method.

## You write

```csharp
using Inquiry.Entities;

[InquiryTable("Documents")]
public sealed class Document
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = "";

    // Keep rows where IsPublished is true (the default).
    [InquiryGlobalFilter]
    [InquiryColumn]
    public bool IsPublished { get; set; }
}

public partial class DocumentStore : InquiryStore<Document>
{
    // Auto-composes WHERE IsPublished = 1
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Document>> SelectAllAsync(CancellationToken ct = default);

    // Same — the filter AND-composes with the field predicate
    [InquirySelectAllByField("Title")]
    public partial Task<IReadOnlyList<Document>> SelectByTitleAsync(string title, CancellationToken ct = default);

    // Aggregates filter too: SELECT COUNT(*) … WHERE IsPublished = 1
    [InquiryCount]
    public partial Task<long> CountPublishedAsync(CancellationToken ct = default);
}
```

### Keeping the *false* rows

Some flags are negative — you want to keep rows where the flag is **off**. Set `KeepWhen = false`:

```csharp
// Keep rows where IsArchived is false (the unarchived ones).
[InquiryGlobalFilter(KeepWhen = false)]
[InquiryColumn]
public bool IsArchived { get; set; }
```

This emits `WHERE IsArchived = 0` on every select.

### Multiple filters

An entity can declare several `[InquiryGlobalFilter]` columns. They are **AND-composed**, so all conditions must hold for a row to be visible:

```csharp
[InquiryGlobalFilter] [InquiryColumn] public bool IsActive { get; set; }
[InquiryGlobalFilter] [InquiryColumn] public bool IsPublished { get; set; }
// → WHERE IsActive = 1 AND IsPublished = 1
```

## The generator emits

The keep literal is per-dialect — SQL Server / SQLite / MySQL / Oracle use `1`/`0`, PostgreSQL uses `TRUE`/`FALSE`.

```csharp
private const string _sqlSelectAll =
    "SELECT \"Id\", \"Title\", \"IsPublished\" FROM \"Documents\" WHERE \"IsPublished\" = 1";

private const string _sqlSelectByTitle =
    "SELECT \"Id\", \"Title\", \"IsPublished\" FROM \"Documents\" " +
    "WHERE \"Title\" = @Title AND \"IsPublished\" = 1";

private const string _sqlCount =
    "SELECT COUNT(*) FROM \"Documents\" WHERE \"IsPublished\" = 1";
```

## No silent bypass

Unlike soft delete — which has a per-method `IncludeDeleted = true` opt-out — a global filter has **no per-method bypass**. That's deliberate: a tenant-isolation or publish filter is a safety boundary you don't want a stray flag to drop. When you genuinely need an unfiltered read (an admin "all tenants" view, a back-office report), reach for an [ad-hoc query](ad-hoc-dtos.md) or a hand-written [`InquiryCommand`](crud.md), where the absence of the filter is explicit and reviewable.

This also means the filter and soft delete compose cleanly: on an entity with **both**, `IncludeDeleted = true` drops only the soft-delete term — the global filter still applies, so an "include deleted" read still respects tenant isolation.

```csharp
// Entity has both [InquirySoftDelete] IsDeleted and [InquiryGlobalFilter] IsPublished.

[InquirySelectAll]                       // WHERE IsDeleted = 0 AND IsPublished = 1
[InquirySelectAll(IncludeDeleted = true)] // WHERE IsPublished = 1  (soft-delete term dropped, filter kept)
```

## Composition with other features

A global filter composes correctly with everything else the generator emits, using the same active-row machinery as soft delete:

- **Pagination.** AND-composed *before* `ORDER BY` / `LIMIT`.
- **Keyset paging.** Appended after the keyset cursor predicate.
- **Aggregates.** `SELECT COUNT(*) / SUM(…) FROM … WHERE <filter>`.
- **Projections.** A [projection](projections.md) over a filtered entity composes the filter even though the projected column subset doesn't include the filter column.
- **Optimistic concurrency.** Updates and deletes still check the row-version column alongside the filter.

## Per-dialect literal

| Dialect | Keep-true literal | Column type |
|---|---|---|
| Sqlite | `IsActive = 1` | `INTEGER NOT NULL` |
| SQL Server | `[IsActive] = 1` | `BIT NOT NULL` |
| PostgreSQL | `"IsActive" = TRUE` | `BOOLEAN NOT NULL` |
| MySQL | `` `IsActive` = 1 `` | `TINYINT(1) NOT NULL` |
| Oracle | `"ISACTIVE" = 1` | `NUMBER(1) NOT NULL` |

## Rules

A `[InquiryGlobalFilter]` column must be a **non-nullable `bool`**, and it cannot double as the key, a generated / database-default column, the [soft-delete](soft-delete.md) indicator, or a [concurrency token](concurrency.md) — those machineries own the column's value. Violations are a compile error (**`INQ059`**).

## When to reach for it

A global filter is right when a subset of rows should be invisible to *normal* reads by default and the distinction is a stable boolean — tenancy, activation, publish state, archival. When the gate is a soft *deletion* (with delete/restore semantics and an admin "see deleted" view), use [soft delete](soft-delete.md) instead; it adds the delete→update routing and the `IncludeDeleted` opt-out a filter intentionally omits.

## See also

- [Soft delete](soft-delete.md) — the same active-row machinery, specialized for deletion with a per-method opt-out.
- [CRUD](crud.md) — the baseline operations the filter extends.
- [Ad-hoc DTOs](ad-hoc-dtos.md) — the escape hatch for a deliberately unfiltered read.
