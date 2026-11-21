# Set-based mutations (UPDATE/DELETE by predicate)

Update or delete rows matching a WHERE predicate **without loading entities** — the
EF Core `ExecuteUpdate`/`ExecuteDelete` analog, with the SQL baked at compile time. Both
operations return the affected row count. An update can instead
[return the values it changed](#return-the-changed-values).

## You write

```csharp
public partial class CustomerStore : InquiryStore<Customer>
{
    // Leading parameters map to SET columns by name. Trailing parameters
    // bind the [InquiryWhere] criteria positionally.
    [InquiryUpdate]
    [InquiryWhere("LastSeen", Compare.LessThan)]
    public partial Task<int> DeactivateStaleAsync(bool isActive, DateTime cutoff, CancellationToken ct = default);

    [InquiryDelete]
    [InquiryWhere("IsActive", Compare.Equal)]
    [InquiryWhere("CreatedAt", Compare.LessThan)]
    public partial Task<int> PurgeInactiveBeforeAsync(bool isActive, DateTime cutoff, CancellationToken ct = default);
}
```

For an update, Inquiry calculates how many trailing parameters the predicates consume, then maps every
leading parameter to a mutable entity property or column with the same name (case-insensitive). In the
example, `isActive` maps to `Customer.IsActive`; `cutoff` belongs to the predicate. This makes a
single-column partial update concise:

```csharp
[InquiryUpdate]
[InquiryWhere(nameof(Member.Key))]
public partial Task<int> UpdateEmailAsync(
    string email,
    int key,
    CancellationToken ct = default);
```

This emits the equivalent of `UPDATE Member SET Email = @Email WHERE Key = @Key`. Add more leading
parameters to update more columns. Each update parameter must have a unique name that resolves to its
target property or mapped column.

These methods choose columns at compile time; null is a supplied value, not an omission marker.
For variable JSON fields, use the [presence-aware SQL recipe](partial-updates.md), which distinguishes
omitted fields, SQL NULL, and supplied values without loading the row first.

`[InquiryWhere]` works exactly as on
[predicate selects](crud.md): repeat it per criterion, criteria combine in declaration order
(AND by default, `Or = true` to join with OR), and parameters bind positionally unless the criteria
[name their parameters](query-composition.md#named-criteria-on-set-based-updates) —
`Compare.Between` / `Compare.NotBetween` consume two, `Compare.In` / `Compare.NotIn` one collection
(expanded at run time; an empty `NotIn` matches every row), `IsNull`/`IsNotNull` none. The full operator
set is the comparisons (`Equal`, `NotEqual`, `GreaterThan`, …), `Like` / `NotLike` (string fields), `In`
/ `NotIn`, `Between` / `NotBetween`, and the null checks.

## Rules

- A delete returns `Task<int>` (rows affected). An update returns `Task<int>`, or the changed values
  as described [below](#return-the-changed-values).
- **At least one `[InquiryWhere]` is required** for parameter-inferred updates and predicate deletes.
  `[InquiryUpdate]` without a predicate remains the entity-by-key update. A targetless
  `[InquiryDelete]` is a compile error; use `[InquiryDeleteAll]` for an explicit table-wide delete.
- `[InquiryUpdate]`'s SET fields must be ordinary mutable columns: not the key, not
  database-generated, not the soft-delete indicator, not a concurrency token.
- `[InquiryUpdate(StampModified = true)]` also writes the entity's `[InquiryModifiedAt]` and
  `[InquiryModifiedBy]` columns, with `UtcNow` and `InquiryAuditContext.CurrentUser`. See
  [Stamping modified columns](#stamping-modified-columns).
- **Concurrency-token entities are rejected** for both operations (same rationale as batch
  mutations: a set-based statement cannot check per-row tokens).

## Stamping modified columns

A set-based update does not write auditing columns unless you opt in. With `StampModified = true`, the
generated SET clause appends one parameter per `[InquiryModifiedAt]` and `[InquiryModifiedBy]` column,
after the method's own assignments. The method declares no parameters for them, and the binder fills
them with `UtcNow` and `InquiryAuditContext.CurrentUser`:

```csharp
[InquiryUpdate(StampModified = true)]
[InquiryWhere(nameof(Member.Id))]
public partial Task<int> SetStatusAsync(string status, long id, CancellationToken ct = default);
// UPDATE [Member] SET [Status] = @Status, [TStamp] = @TStamp, [LastUpdatedBy] = @LastUpdatedBy WHERE [Id] = @Id

[InquiryUpdate(StampModified = true)]
[InquirySet(nameof(Member.Score), "{Score} + @delta")]
[InquiryWhere(nameof(Member.Id))]
public partial Task<int> AddScoreAsync(int delta, long id, CancellationToken ct = default);
// UPDATE [Member] SET [Score] = [Score] + @delta, [TStamp] = @TStamp, [LastUpdatedBy] = @LastUpdatedBy WHERE [Id] = @Id
```

Without `StampModified`, the generated SQL is unchanged. A criterion on a stamped column binds under a
new name, such as `@TStamp2`, so it cannot collide with the stamp. A method that opts in and also
assigns a modified column, through a parameter or `[InquirySet]`, reports
[INQ118](../diagnostics.md#inq118). Set-based deletes and soft deletes never stamp. See
[Auditing](auditing.md#set-based-updates).

## Return the changed values

An update can return the rows it changed, as they are after the update. Use this for an atomic
read-modify-write, such as incrementing a counter and reading the new value, or claiming rows and
reading what was claimed. The values come from the same statement as the update, so another writer
cannot change them in between.

```csharp
public partial class UserStore : InquiryStore<User>
{
    // The new count, or null when no row matched.
    [InquiryUpdate]
    [InquirySet(nameof(User.AccessFailedCount), "{AccessFailedCount} + 1")]
    [InquiryWhere(nameof(User.Id))]
    public partial Task<int?> IncrementFailuresAsync(Guid id, CancellationToken ct = default);

    // One entry per updated row.
    [InquiryUpdate]
    [InquiryWhere(nameof(User.TenantId))]
    public partial Task<IReadOnlyList<User>> LockTenantAsync(
        DateTimeOffset? lockoutEnd, Guid tenantId, CancellationToken ct = default);
}
```

| Return type | Result |
|---|---|
| `Task<int>` | The affected-row count. |
| `Task<T?>` | The updated row or value, or `null` when no row matched. |
| `Task<IReadOnlyList<T>>` | One entry per updated row, in no particular order. Empty when no row matched. |

`T` is one of these:

- The entity.
- An [`[InquiryProjection]`](projections.md) of the entity. Only the projection's columns are returned.
- The type of the single column that the update sets, such as `int` for `AccessFailedCount`. A value
  type must be nullable in the single-row form, such as `int?`. To read a column that the update does
  not set, such as the key of a claimed row, return the entity or a projection.

The single-row form, `Task<T?>`, needs criteria that can match at most one row: `Equal` on every
primary-key column, or `Equal` on a column with `IsUnique = true`, joined with AND. Optional and
negated criteria do not count. The update changes every matching row before any row is read, so
the generator rejects a single-row form that could match more than one. Use the list form for
those updates.

Each provider reads the values in the statement that updates them:

| Provider | How the values are read |
|---|---|
| SQL Server | `OUTPUT INSERTED.*` into a table variable typed from each column's `SqlType`, then a `SELECT` from it. The table variable also lets the update run on a table with triggers. |
| PostgreSQL, SQLite | `UPDATE ... RETURNING`. |
| Oracle | A PL/SQL block collects the `ROWID` of each updated row, then opens a cursor over those rows while the update still holds their locks. One call can update at most 32,767 rows. A larger update fails, and Oracle rolls back the whole block. |
| MySQL, MariaDB | Not supported. Neither has `UPDATE ... RETURNING`, so these return types report [INQ039](../diagnostics.md#inq039). Return `Task<int>`, or read the rows with a [locking select](row-locking.md) in a transaction and then update them. |

A single-row form that can match more than one row, a value return on an update that sets more than
one column, and a value type that does not match the column all report
[INQ096](../diagnostics.md#inq096).

## Soft-delete entities

On a soft-delete entity, `[InquiryDelete]` emits the **soft UPDATE form** (sets the
indicator) and both operations compose the active-row filter into the WHERE — already-deleted
rows are never updated or re-deleted. `[InquiryHardDelete]` forces a literal
`DELETE`, matching the key-delete behavior.

## Delete targeting

Use the narrowest form that expresses the intended target:

```csharp
// One row by primary key.
[InquiryDelete]
public partial Task<bool> DeleteByKeyAsync(int key, CancellationToken ct = default);

// Every row matching a predicate.
[InquiryDelete]
[InquiryWhere(nameof(Member.Email))]
public partial Task<int> DeleteByEmailAsync(string email, CancellationToken ct = default);

// Every row whose key is in the supplied collection.
[InquiryDelete]
[InquiryWhere(nameof(Member.Key), Compare.In)]
public partial Task<int> DeleteByKeysAsync(IReadOnlyList<int> keys, CancellationToken ct = default);

// Every row in the table. The explicit attribute is required.
[InquiryDeleteAll]
public partial Task<int> DeleteAllAsync(CancellationToken ct = default);
```

`[InquiryDeleteAll]` performs the normal soft-delete update for a soft-delete entity. Use
`[InquiryHardDeleteAll]` to emit a literal table-wide `DELETE`. Write-enforced global filters still apply.

## Transactions and timeouts

Like every generated method, set-based mutations participate in ambient Inquiry transactions and
honor `InquiryOptions.DefaultCommandTimeout`. A predicate mutation is a single statement, so it is
atomic on its own.

## Advanced composition

See [Query composition](query-composition.md) for reusable criteria, grouped and optional predicates,
expression assignments, positional parameter order, and a safely bound handwritten report.
