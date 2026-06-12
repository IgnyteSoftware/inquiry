# Auditing timestamps

Track *when* rows were created and last written without writing any plumbing: mark a `DateTime` or `DateTimeOffset` property with `[InquiryCreatedAt]` or `[InquiryModifiedAt]` and the generated store methods maintain them.

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

## Rules and limits

- Property type must be `DateTime` or `DateTimeOffset` (nullable allowed); anything else — or combining with a key, `IsGenerated`, `UseDatabaseDefault`, `[InquirySoftDelete]`, or `[InquiryConcurrencyToken]` — is a build-time error (`INQ049`). At most one of each per entity (`INQ050`).
- Timestamps are generated **client-side in UTC** (`DateTime.UtcNow` / `DateTimeOffset.UtcNow`). If you need database-clock stamping, use `[InquiryColumn(DefaultExpression = ...)]` instead of these attributes.
- **Set-based mutations (`[InquiryUpdateWhere]`) and soft-delete/restore do not touch auditing columns** — they update rows without materializing entities. Stamp explicitly (add `ModifiedAt` to the SET fields) when that matters.
- A who-changed-it counterpart (`[InquiryModifiedBy]`) needs an ambient user accessor and is not part of v1; the audit-trail interceptor on the roadmap covers full change history.

## See also

- [CRUD](crud.md) — the generated method shapes these stamps plug into.
- [Soft delete](soft-delete.md) — the deletion timestamp variant.
- [Optimistic concurrency](concurrency.md) — version columns, a different kind of "modified" tracking.
