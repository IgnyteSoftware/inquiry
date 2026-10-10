# Global query filters

Use a boolean filter such as `IsPublished` or `IsActive` for shared visibility rules. A shared
boolean does not identify the requesting tenant. Use a parameterized filter for request-specific
scoping, and application authorization or database row-level security for access control.

## Static visibility

```csharp
[InquiryColumn, InquiryGlobalFilter]
public bool IsPublished { get; set; }
```

Generated reads add `IsPublished = 1` (or the provider's boolean literal). Set `KeepWhen = false`
to keep false rows instead. Several filters combine with AND. Static filters require a non-nullable
boolean column that is not a key, generated/default column, soft-delete marker, or concurrency
token (`INQ059`). The property still needs `[InquiryColumn]`.

## Runtime-parameterized filters

Use the same tenant key in the model and request scope:

```csharp
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryTable("Documents")]
public sealed class Document
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = "";

    [InquiryColumn]
    [InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]
    public long TenantId { get; set; }
}

public partial class DocumentStore : InquiryStore<Document>
{
    [InquirySelectAll]
    public partial Task<IReadOnlyList<Document>> AllAsync(CancellationToken ct = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(Document document, CancellationToken ct = default);
}

// In the request handler, after authenticating and authorizing the tenant:
using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
{
    var documents = await store.AllAsync(ct);
}
```

Generated reads bind `TenantId = @__gf_TenantId` at execution. The value flows across `await`
within the current async flow. An absent scope, missing key, null, or wrong value type throws
`InquiryFilterValueMissingException` before a filtered command runs.

These limits apply to the example:

- Inserts and bulk inserts do not stamp or validate `TenantId`. A missing scope does not reject
  those operations, and a conflicting supplied tenant value is still inserted.
- `EnforceOnWrites` checks the row's existing tenant. By default, an update can reassign the
  tenant column. Opt into [column protection](#protecting-an-enforced-column) to exclude that assignment.
- Upsert with write enforcement is rejected (`INQ095`). Use separate insert and update operations.
- Raw SQL and stored procedures do not inherit these predicates or require this ambient scope.
- Eager loading rejects parameterized filters anywhere in its relation tree (`INQ093`). Use
  non-eager reads with explicit application composition.

Parameterized filters accept non-nullable mapped scalar types, including a composite-key component.
Do not also set `KeepWhen`. A blank `ContextKey`, nullable column, or conflicting owned column role
produces `INQ093`. String equality follows the database collation, including its case and padding
rules. Prefer integral or Guid tenant identifiers when that distinction matters.

## Scope disposal

Dispose nested scopes before their parent. Out-of-order disposal throws
`InvalidOperationException` and leaves the current value unchanged; close the inner scope,
then retry the outer one. If a body exception leaves an inner scope open, the
outer disposal exception can replace it; use nested `using` blocks. Repeated disposal, copied scope values, and a default scope do
nothing when that scope is no longer active in the current async flow.

Disposal changes only the current async flow. A child inherits its parent's values, but
closing an inherited scope in the child does not close it in the parent. Prefer lexical
`using` blocks and await child work before leaving them. Filter and audit scopes use the
same rules.

## Enforcing a filter on writes

`EnforceOnWrites = true` adds the filter to generated key-based update, delete, and restore
predicates, batch updates, hard predicate deletion, and table-wide deletion. An out-of-scope
mutation affects zero rows. Zero does not distinguish a hidden row from a missing row or stale
concurrency token. Returning mutations return null in those cases.

The flag defaults to false. Generated predicate updates and soft predicate deletes already use
read filters, even without write enforcement. Hard deletion and restore still reach soft-deleted
rows while preserving enforced tenant predicates.

## Protecting an enforced column

```csharp
[InquiryColumn]
[InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true, PreventReassignment = true)]
public long TenantId { get; set; }
```

`PreventReassignment` omits this column from single-entity and batch-update SET clauses.
For example, under tenant 7, updating an existing tenant-7 document whose input contains
`TenantId = 8` updates its other fields but keeps the stored tenant at 7. The input object is not
rewritten. A returning update reports the stored tenant. Protected composite-key columns remain
key predicates; they are never SET.

Explicit partial-update or `InquirySet` assignments to the protected column fail with `INQ044`.
The policy requires a valid filter and `EnforceOnWrites = true` (`INQ102`). An entity update with
no writable columns left fails with `INQ096`. The policy defaults to false.

This policy protects reassignment only. Insert, batch-insert, and bulk-insert values remain
caller-supplied; there is no automatic stamping, ambient-value check, or conflict validation.
Validate and assign tenant values in the application's create path. Database RLS `WITH CHECK`
can enforce new values for callers outside generated stores too.

## Operation and bypass matrix

| Operation | Filter behavior | Column protection |
|---|---|---|
| Generated selects, paging, counts, aggregates, projections | Read filters apply; a valid named ignore can remove its named term | Not applicable |
| Entity update, including returning | Enforced filters plus key/token/active-row predicates | Protected fields excluded from SET |
| Batch entity update | Enforced filters apply across row, array, and set-based strategies | Protected fields excluded from SET |
| Predicate update | Read filters apply | Explicit protected target rejected |
| Key soft/hard delete and restore | Enforced filters apply; operation owns soft-delete state | No tenant reassignment |
| Soft predicate delete | Read filters apply | No tenant reassignment |
| Hard predicate or table-wide delete | Enforced filters apply | No tenant reassignment |
| Insert, batch insert, bulk insert | No filter validation or stamping | Supplied values retained |
| Upsert with an enforced filter | Rejected with INQ095 | No generated write |
| Eager loading | Parameterized filters rejected; static filters supported, named ignore rejected | Not applicable |
| Raw SQL, ad-hoc queries, stored procedures | No generated filter injection | No protection |

Batch/set-based mutation on a concurrency-token entity has separate restrictions (`INQ022`).
A named ignore is read-only and cannot remove write enforcement. The filter policy applies to
generated operations in this matrix; it does not restrict every `IInquiry` command or database user.

## Named filters and per-method bypass

Give a visibility filter a `Name` when selected generated reads should bypass it:

```csharp
[InquiryColumn, InquiryGlobalFilter(Name = "PublishGate")]
public bool IsPublished { get; set; }

// Add this property to Document, then declare this method in DocumentStore:
[InquirySelectAll, InquiryIgnoreFilter("PublishGate")]
public partial Task<IReadOnlyList<Document>> IncludingDraftsAsync(CancellationToken ct = default);
```

An unnamed filter has no generated per-method ignore. Raw SQL and procedures remain outside
this mechanism. Unknown names, unsupported operations, and write-side ignores fail with `INQ091`;
blank or duplicate declared names fail with `INQ092`. Several ignore attributes can remove several
named filters. `IncludeDeleted` removes only the soft-delete term, leaving global filters intact.

## Authorization and database enforcement

Visibility filters choose which rows generated queries see. Write enforcement limits which
existing rows supported mutations reach. Column protection preserves selected update values.
None authenticates a request or grants permission to edit a row inside the current tenant.

Authorize the request before establishing its tenant scope. Use database policies when enforcement
must also cover raw SQL, procedures, inserts, or other clients. See [PostgreSQL row-level security](postgres-rls.md).

## See also

- [Soft delete](soft-delete.md)
- [Optimistic concurrency](concurrency.md)
- [Ad-hoc DTOs](ad-hoc-dtos.md)
