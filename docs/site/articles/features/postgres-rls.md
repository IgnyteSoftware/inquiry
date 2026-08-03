# PostgreSQL row-level security

PostgreSQL can enforce a tenant boundary in the database itself, underneath whatever the application
does. Inquiry's part is small and deliberate: one helper that sets a **transaction-scoped**
configuration parameter your row-level-security policies read back.

```csharp
await using var tx = await inquiry.BeginTransactionAsync();
await tx.SetLocalAsync("app.tenant_id", tenantId);

// Every read and write in this transaction is now constrained by the RLS policy as well as by
// whatever [InquiryGlobalFilter] predicates the generated SQL already carries.
var docs = await store.AllAsync();

await tx.CommitAsync();
```

## Why it only exists on a transaction

`SetLocalAsync` is an extension on `IInquiryTransaction` and nothing else. That is the whole design.

A transaction-scoped setting applied outside a transaction does not reach the query you meant it for.
`SET LOCAL` outside a transaction block emits `WARNING: SET LOCAL can only be used in transaction
blocks` and has no effect, and `set_config(…, is_local => true)` under autocommit expires with the
implicit single-statement transaction that ran it. The policy then reads an unset parameter, and your
query returns **zero rows**.

That is fail-closed, which is the right direction — but it fails *silently*, and the symptom shows up
somewhere else entirely: an unrelated query quietly returning nothing. Requiring a transaction handle
turns a runtime mystery into a compile error.

### Use the handle on the flow that opened it

Generated stores join the transaction through an ambient async-local slot, not through the handle
object. So the setting applies to work on the async flow that began the transaction. Work started on a
flow that did not inherit that slot — a detached `Task.Run`, a fire-and-forget continuation — runs
outside the transaction entirely, does not see the setting, and its reads come back empty.

## Writing the policy

The policy DDL is yours — it belongs in your migration, not in Inquiry. The shape that pairs with
`SetLocalAsync`:

```sql
ALTER TABLE doc ENABLE ROW LEVEL SECURITY;
ALTER TABLE doc FORCE  ROW LEVEL SECURITY;   -- see the gotcha below

CREATE POLICY doc_tenant ON doc
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));
```

The `true` is `missing_ok`. It makes an unset parameter read as `NULL` instead of raising, so the
comparison is `NULL`, no row qualifies, and a request that forgot to call `SetLocalAsync` sees
**nothing** rather than **everything**. Write it that way round.

The `NULLIF(…, '')` matters for the same reason. Once a custom parameter has been set anywhere in a
session it resets to the **empty string** rather than to NULL (see [Lifetime](#lifetime-nothing-to-clean-up)
below), and a bare `tenant_id = current_setting(…)` would then be `tenant_id = ''` — which is `TRUE`
for any row whose `tenant_id` is itself empty. Collapsing `''` back to `NULL` closes that. Belt and
braces: `CHECK (tenant_id <> '')` on the column removes the matching rows from existence.

### Gotcha: `ENABLE` alone exempts the table owner

`ENABLE ROW LEVEL SECURITY` does not apply to the table's owner. If your application connects as the
role that owns its tables — a very common setup — the policy is inert and every query returns every
row, silently. `FORCE ROW LEVEL SECURITY` is what closes that, and you almost certainly want both.

**Superusers bypass RLS entirely**, and `FORCE` does not change that. Never point an application at a
superuser login if you are relying on row-level security.

## Lifetime: nothing to clean up

The setting is written with `set_config(name, value, is_local => true)`, so it is discarded when the
transaction commits **or** rolls back. A pooled connection never carries it into the next transaction.

Do **not** add a compensating `RESET` or `DISCARD`. There is nothing to clean up and it only costs a
round trip.

One observed detail with a sharp edge: once a custom parameter has been set anywhere in a session,
ending the transaction resets it to its *default* — the **empty string** — rather than undefining it.
So `current_setting('app.tenant_id', true)` returns `NULL` on a connection that has never seen the
setting, and `''` on one where a previous transaction set it.

Neither state matches a real tenant id, so a bare `tenant_id = current_setting(…)` policy is
fail-closed against both — **unless some row's `tenant_id` is itself the empty string**, which `''`
matches exactly. That is why the policy above wraps the read in `NULLIF(…, '')`. If you branch on
`IS NULL` anywhere in a policy, note that it will not fire in the post-transaction state.

## Savepoints

Configuration changes are transactional in PostgreSQL, and nested Inquiry transactions are savepoints,
so the two compose the way you would hope. Verified against a live engine:

| Inner action | Effect on the setting |
|---|---|
| Nested transaction sets a new value, then **rolls back** | Reverts to the outer transaction's value |
| Nested transaction sets a new value, then **commits** (releases the savepoint) | Keeps the inner value |

The second row is the one to watch: releasing a savepoint does **not** restore the outer scope's
tenant. If a nested unit of work narrows or changes the tenant, it must either roll back or set the
value back itself.

## Setting several parameters

```csharp
await tx.SetLocalAsync(new Dictionary<string, string>
{
    ["app.tenant_id"] = tenantId,
    ["app.request_id"] = requestId,
});
```

One statement per entry, all scoped to the same transaction.

## Names and values

The setting name must be dot-separated identifiers with at least one dot — `app.tenant_id`, or
`myapp.rls.tenant_id` if you prefer a deeper namespace. PostgreSQL requires the prefix for a custom
parameter; the rest is Inquiry constraining every component to a simple identifier so the name never
needs quoting. Anything else throws `ArgumentException` **before** a statement runs, so a rejected
name never aborts the caller's unit of work.

The value is bound as a command parameter, never interpolated, so caller-supplied data is safe.

## Relationship to `[InquiryGlobalFilter]`

These are two layers of the same boundary, and they are worth having together:

- [`[InquiryGlobalFilter(ContextKey = …)]`](global-filters.md#runtime-parameterized-filters) puts the
  tenant predicate in the generated SQL. It is compile-time, costs nothing at runtime, and applies to
  every generated read — plus every key-based write when
  [`EnforceOnWrites`](global-filters.md#enforcing-a-filter-on-writes) is set.
- RLS puts the same predicate in the database, where it also covers ad-hoc SQL, a psql session, a
  reporting tool, and any code path that never went through a generated store.

The filter is the ergonomic layer; RLS is the one that still holds when something bypasses the ORM.
Neither is a substitute for authorizing the caller for a specific row *within* their own tenant.
