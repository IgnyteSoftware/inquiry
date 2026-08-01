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

Unlike soft delete — which has a per-method `IncludeDeleted = true` opt-out — an **unnamed** global filter has **no per-method bypass**. That's deliberate: a tenant-isolation filter is a safety boundary you don't want a stray flag to drop. When you genuinely need an unfiltered read (an admin "all tenants" view, a back-office report), reach for an [ad-hoc query](ad-hoc-dtos.md) or a hand-written [`InquiryCommand`](crud.md), where the absence of the filter is explicit and reviewable.

## Named filters and per-method bypass

For filters that are conveniences rather than security boundaries — a `PublishGate` an admin view legitimately lists drafts past — give the filter a `Name` and bypass it per method with `[InquiryIgnoreFilter]` (the EF 10 `IgnoreQueryFilters(string[])` analog, fully compile-time):

```csharp
[InquiryColumn("IsPublished"), InquiryGlobalFilter(Name = "PublishGate")]
public bool IsPublished { get; set; }

[InquiryColumn("IsActive"), InquiryGlobalFilter]   // unnamed — never bypassable
public bool IsActive { get; set; }

// In the store:
[InquirySelectAll]                                  // WHERE IsPublished = 1 AND IsActive = 1
[InquirySelectAll, InquiryIgnoreFilter("PublishGate")] // WHERE IsActive = 1
```

The rules keep the bypass exactly as safe as the rest of the mechanism:

- Only a filter with a **declared `Name`** can be bypassed; the ignore attribute names the filter, never the column. Unnamed filters always stay composed.
- The name is resolved **entirely at generation time** — the method still gets a const SQL string with the term simply absent. On any generated operation, an unknown or unnamed filter name is a **build error** (`INQ091`) that drops the method, never a silently ignored attribute; a blank or duplicated `Name` on the entity is one too (`INQ092`, with duplicated names additionally rendered non-bypassable so a suppressed diagnostic cannot widen the bypass). The generator cannot see hand-written methods — an ignore attribute on a method with no Inquiry operation attribute is outside its reach and does nothing.
- One attribute per filter; apply it multiple times to bypass several named filters.
- Valid on the read operations that compose the filter — selects, paged/keyset reads, `Count`, `Exists`, aggregates, group counts, full-text search. Eager-loading methods reject it (`INQ091`) in v1, and it never applies to writes.
- `IncludeDeleted` and a named bypass compose independently: each drops exactly its own term.

## Runtime-parameterized filters

For a filter whose value is per-request rather than a constant — the multi-tenant shape — set `ContextKey` instead of relying on the bool column form:

```csharp
[InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId")]
public long TenantId { get; set; }
```

Every generated read now composes `"TenantId" = @__gf_TenantId`, and the value is bound **at execute time** from the ambient `InquiryFilterContext` — set once per request, typically in middleware, exactly like `InquiryAuditContext`:

```csharp
using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
{
    var docs = await store.AllAsync();   // WHERE "TenantId" = @__gf_TenantId AND …
}
```

The rules:

- The SQL is still a compile-time const; only the parameter's value is runtime. The value flows across `await` via `AsyncLocal` and is isolated per async flow.
- **A missing ambient value throws `InquiryFilterValueMissingException` before the command executes.** No scope, an absent key, or a wrong-typed value never binds `NULL` or returns an empty result — an empty result would be indistinguishable from working tenant isolation, which is the failure this mode exists to prevent. Exception messages name the key, never the value.
- The column may be any **non-nullable mapped scalar** — `Guid`, `long`, `string`, an enum, a converter column — and, unlike the constant-bool form, it **may be a key component** (a tenant id inside a composite key is the norm). Explicitly setting `KeepWhen` alongside `ContextKey` is a build error (`INQ093`), as is a blank key, a nullable column, or a column whose role other machinery owns.
- `ContextKey` is the value's identity; `Name` remains the bypass identity. A tenant boundary should set `ContextKey` and leave `Name` unset — then nothing can bypass it. A filter with both is bypassable like any named filter, and the bypassing method's SQL loses the term *and* its binder loses the parameter together.
- **Key-based writes** (insert, update, delete, restore, upsert) do not compose the filter in this release — opt-in write-side enforcement is a separate roadmap feature. **Set-based predicate writes** (`[InquiryUpdateWhere]` and the soft form of `[InquiryDeleteWhere]`) compose it exactly as they always have, so those statements ARE tenant-scoped and throw `InquiryFilterValueMissingException` without an ambient scope. Eager-loading methods reject entities with parameterized filters anywhere in their relation tree (`INQ093`) — their shared relation consts cannot bind per-method parameters yet; use non-eager reads with these filters for now.
- For a `string` tenant key, remember the equality inherits the column's **collation**: on a case-insensitive default (SQL Server, MySQL) `'acme'`, `'ACME'`, and — with SQL Server's trailing-blank semantics — `'acme '` all match. Prefer a `Guid` or integral tenant key, or a binary/case-sensitive collation, when tenant identifiers are user-influenced.

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
- **Optimistic concurrency.** A [concurrency token](concurrency.md) on a filtered entity is unaffected — key-based updates/deletes still match/advance the row-version column.

## Where the filter is composed

A global filter is composed into **exactly the same statements as the [soft-delete](soft-delete.md) active filter** — that invariant is the whole point of sharing the machinery:

- **Composed** (the filter participates): every read (SELECT / COUNT / aggregate, including paged, keyset, and projection reads), set-based `[InquiryUpdateWhere]`, and set-based predicate soft-deletes. A set-based update therefore can't touch a row the filter hides.
- **Not composed** (the statement targets rows by key, or deletes literally): key-based `UPDATE` / `DELETE`, key-based soft-delete / restore, and hard `[InquiryDeleteWhere]`.

This mirrors [soft delete](soft-delete.md) and EF Core's `HasQueryFilter`: it's a **query** filter. So if you know a row's primary key you can still update or delete it by key even when the filter would hide it from reads.

If you use `[InquiryGlobalFilter]` for **multi-tenant isolation**, treat it as read-side scoping, not write authorization. Key-based mutations aren't filtered, so enforce the boundary on by-key writes separately — at the service layer, or by routing writes through `[InquiryUpdateWhere]` / a predicate that includes the tenant column (these *are* filtered). Read-side filtering keeps other tenants' rows out of query results; it is not, on its own, a write-authorization mechanism.

## Per-dialect literal

| Dialect | Keep-true literal | Column type |
|---|---|---|
| Sqlite | `IsActive = 1` | `INTEGER NOT NULL` |
| SQL Server | `[IsActive] = 1` | `BIT NOT NULL` |
| PostgreSQL | `"IsActive" = TRUE` | `BOOLEAN NOT NULL` |
| MySQL | `` `IsActive` = 1 `` | `TINYINT(1) NOT NULL` |
| Oracle | `IsActive = 1` (unquoted) | `NUMBER(1) NOT NULL` |

## Rules

A `[InquiryGlobalFilter]` column must be a **non-nullable `bool`**, and it cannot double as the key, a generated / database-default column, the [soft-delete](soft-delete.md) indicator, or a [concurrency token](concurrency.md) — those machineries own the column's value. Violations are a compile error (**`INQ059`**).

## When to reach for it

A global filter is right when a subset of rows should be invisible to *normal* reads by default and the distinction is a stable boolean — tenancy, activation, publish state, archival. When the gate is a soft *deletion* (with delete/restore semantics and an admin "see deleted" view), use [soft delete](soft-delete.md) instead; it adds the delete→update routing and the `IncludeDeleted` opt-out a filter intentionally omits.

## See also

- [Soft delete](soft-delete.md) — the same active-row machinery, specialized for deletion with a per-method opt-out.
- [CRUD](crud.md) — the baseline operations the filter extends.
- [Ad-hoc DTOs](ad-hoc-dtos.md) — the escape hatch for a deliberately unfiltered read.
