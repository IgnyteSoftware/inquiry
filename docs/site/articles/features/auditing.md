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

In ASP.NET Core apps, the `Ignyte.Inquiry.AspNetCore` package wraps this in ready-made middleware — `app.UseInquiryAuditContext()` opens the scope per request, defaulting the user to the `ClaimTypes.NameIdentifier` claim (register it after `UseAuthentication`; pass a `Func<HttpContext, string?>` to resolve the user differently).

## Endpoints without a signed-in user

Some endpoints authenticate without a principal, such as a kiosk or device token that the handler checks, or a webhook signature. Give those endpoints an audit user as endpoint metadata, instead of a `BeginScope` call in each handler:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseInquiryAuditContext();   // after UseRouting, so the endpoint is known

var kiosk = app.MapGroup("/kiosk").WithInquiryAuditUser("system:kiosk");
kiosk.MapPost("/sessions/{id}/abandon", AbandonSession);   // inserts stamp "system:kiosk"

app.MapPost("/webhooks/billing", HandleBilling)
   .WithInquiryAuditUser("system:billing-webhook", overrideResolver: true);
```

For MVC, put `[InquiryAuditUser("system:kiosk")]` on the controller or action. `WithInquiryAuditUser` works on a route group, a single endpoint, or any other endpoint convention builder.

The middleware picks the user for each request in this order:

| Order | Source | Applies when |
|---|---|---|
| 1 | A scope a handler opens with `InquiryAuditContext.BeginScope` | Always, inside that scope. The inner scope replaces the middleware's scope until it is disposed. |
| 2 | Endpoint metadata with `overrideResolver: true` (`OverrideResolver = true` on the attribute) | Always. The middleware does not call the resolver. |
| 3 | The user resolver, which is the signed-in principal by default | The resolver returns a value other than `null`. |
| 4 | Endpoint metadata without `overrideResolver` | The resolver returns `null`. |

When an endpoint gets the metadata more than once, for example from its group and from the endpoint itself, the value closest to the endpoint wins. An endpoint with no metadata behaves as before.

**Middleware order.** The middleware reads the metadata from the routed endpoint, so it must run after `UseRouting`. When `UseInquiryAuditContext` runs before routing, it does not see the metadata, and requests with no signed-in user get a `null` audit user. A `WebApplication` that never calls `UseRouting` adds routing at the start of the pipeline, so the metadata works there too.

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

## Rules and limits

- A timestamp column must be `DateTime`/`DateTimeOffset`; a user column must be `string` (nullable allowed). Combining either with a key, `IsGenerated`, `UseDatabaseDefault`, `[InquirySoftDelete]`, or `[InquiryConcurrencyToken]` is a build-time error (`INQ049` for timestamps, `INQ055` for users). At most one of each per entity (`INQ050` / `INQ056`).
- Timestamps are generated **client-side in UTC**; user values come from `InquiryAuditContext.CurrentUser` (null when no scope is open). For database-clock stamping, map an ordinary column with `[InquiryColumn(DefaultExpression = ..., UseDatabaseDefault = true)]` instead. `DefaultExpression` alone still sends the property value. See [Database defaults](../entity-mapping.md#database-defaults).
- **Predicate updates (`[InquiryUpdate]` plus `[InquiryWhere]`) and soft-delete/restore do not touch auditing columns** — they update rows without materializing entities. Add an audit-column parameter explicitly when that matters.
- For a full who/when/what change *history*, see the audit-trail interceptor on the roadmap.

## See also

- [CRUD](crud.md) — the generated method shapes these stamps plug into.
- [Soft delete](soft-delete.md) — the deletion timestamp variant.
- [Optimistic concurrency](concurrency.md) — version columns, a different kind of "modified" tracking.
