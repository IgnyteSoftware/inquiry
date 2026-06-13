# Auditing timestamps & users

Track *when* rows were created and last written — and *who* did it — without writing any plumbing: mark a `DateTime`/`DateTimeOffset` property with `[InquiryCreatedAt]`/`[InquiryModifiedAt]`, or a `string` property with `[InquiryCreatedBy]`/`[InquiryModifiedBy]`, and the generated store methods maintain them.

## You write

```csharp
using Inquiry.Entities;

[InquiryTable("Documents")]
public sealed class Document
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = "";

    [InquiryCreatedAt]  public DateTime CreatedAt  { get; set; }
    [InquiryModifiedAt] public DateTime ModifiedAt { get; set; }
}
```

No other changes — insert/update/upsert methods on the entity's store now stamp the columns before binding. The stamps **mutate the entity**, so the caller sees the written values after the call.

## Semantics

| Operation | `[InquiryCreatedAt]` | `[InquiryModifiedAt]` |
|---|---|---|
| Insert / batch insert | `UtcNow` **when unset** (default / `null`); a supplied value (imports, backfills) is kept | `UtcNow`, always |
| Update / batch update | **Untouched** — excluded from the UPDATE SET *and* the bind list | `UtcNow`, always |
| Upsert | Stamped when unset; only the insert branch writes it (the conflict branch's SET excludes it) | `UtcNow`, always |

The SET exclusion is the load-bearing detail: updating an entity instance you *constructed* (whose `CreatedAt` is `default`) cannot clobber the stored creation time, because the generated UPDATE never references the column.

```sql
-- generated for the entity above
UPDATE "Documents" SET "Title" = @Title, "ModifiedAt" = @ModifiedAt WHERE "Id" = @Id
```

## Who: `[InquiryCreatedBy]` / `[InquiryModifiedBy]`

The same model, for the *user* instead of the time. Mark `string` properties and the stamps come from an **ambient current-user** value you set once per request:

```csharp
[InquiryTable("Documents")]
public sealed class Document
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = "";

    [InquiryCreatedBy]  public string? CreatedBy  { get; set; }
    [InquiryModifiedBy] public string? ModifiedBy { get; set; }
}
```

```csharp
// Set the ambient user for the unit of work (e.g. ASP.NET Core middleware).
using (InquiryAuditContext.BeginScope(httpContext.User.Identity?.Name))
{
    await documents.InsertAsync(doc);   // CreatedBy + ModifiedBy stamped from the scope
}
```

`InquiryAuditContext.CurrentUser` flows across `await` via `AsyncLocal`, so concurrent requests stay isolated; `BeginScope` returns a disposable that restores the previous value (scopes nest). The **created/modified semantics are identical to the timestamps** — `CreatedBy` is stamped on insert only when unset (null or empty) and excluded from every UPDATE SET; `ModifiedBy` is stamped on every insert/update/upsert.

## Rules and limits

- A timestamp column must be `DateTime`/`DateTimeOffset`; a user column must be `string` (nullable allowed). Combining either with a key, `IsGenerated`, `UseDatabaseDefault`, `[InquirySoftDelete]`, or `[InquiryConcurrencyToken]` is a build-time error (`INQ049` for timestamps, `INQ055` for users). At most one of each per entity (`INQ050` / `INQ056`).
- Timestamps are generated **client-side in UTC**; user values come from `InquiryAuditContext.CurrentUser` (null when no scope is open). For database-clock stamping use `[InquiryColumn(DefaultExpression = ...)]` instead.
- **Set-based mutations (`[InquiryUpdateWhere]`) and soft-delete/restore do not touch auditing columns** — they update rows without materializing entities. Stamp explicitly (add the column to the SET fields) when that matters.
- For a full who/when/what change *history*, see the audit-trail interceptor on the roadmap.

## See also

- [CRUD](crud.md) — the generated method shapes these stamps plug into.
- [Soft delete](soft-delete.md) — the deletion timestamp variant.
- [Optimistic concurrency](concurrency.md) — version columns, a different kind of "modified" tracking.
