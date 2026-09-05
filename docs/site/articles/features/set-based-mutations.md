# Set-based mutations (UPDATE/DELETE by predicate)

Update or delete rows matching a WHERE predicate **without loading entities** — the
EF Core `ExecuteUpdate`/`ExecuteDelete` analog, with the SQL baked at compile time. Both
operations return the affected row count.

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

`[InquiryWhere]` works exactly as on
[predicate selects](crud.md): repeat it per criterion, criteria combine in declaration order
(AND by default, `Or = true` to join with OR), and parameters bind positionally —
`Compare.Between` / `Compare.NotBetween` consume two, `Compare.In` / `Compare.NotIn` one collection
(expanded at run time; an empty `NotIn` matches every row), `IsNull`/`IsNotNull` none. The full operator
set is the comparisons (`Equal`, `NotEqual`, `GreaterThan`, …), `Like` / `NotLike` (string fields), `In`
/ `NotIn`, `Between` / `NotBetween`, and the null checks.

## Rules

- The method must return `Task<int>` (rows affected).
- **At least one `[InquiryWhere]` is required** for parameter-inferred updates and predicate deletes.
  `[InquiryUpdate]` without a predicate remains the entity-by-key update. A targetless
  `[InquiryDelete]` is a compile error; use `[InquiryDeleteAll]` for an explicit table-wide delete.
- `[InquiryUpdate]`'s SET fields must be ordinary mutable columns: not the key, not
  database-generated, not the soft-delete indicator, not a concurrency token.
- **Concurrency-token entities are rejected** for both operations (same rationale as batch
  mutations: a set-based statement cannot check per-row tokens).

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
