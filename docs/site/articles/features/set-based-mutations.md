# Set-based mutations (UPDATE/DELETE by predicate)

Update or delete rows matching a WHERE predicate **without loading entities** — the
EF Core `ExecuteUpdate`/`ExecuteDelete` analog, with the SQL baked at compile time. Both
operations return the affected row count.

## You write

```csharp
public partial class CustomerStore : InquiryStore<Customer>
{
    // First parameter feeds the SET column (declared on the attribute);
    // remaining parameters bind the [InquiryWhere] criteria positionally.
    [InquiryUpdateWhere("IsActive")]
    [InquiryWhere("LastSeen", Compare.LessThan)]
    public partial Task<int> DeactivateStaleAsync(bool isActive, DateTime cutoff, CancellationToken ct = default);

    [InquiryDeleteWhere]
    [InquiryWhere("IsActive", Compare.Equal)]
    [InquiryWhere("CreatedAt", Compare.LessThan)]
    public partial Task<int> PurgeInactiveBeforeAsync(bool isActive, DateTime cutoff, CancellationToken ct = default);
}
```

`[InquiryWhere]` works exactly as on
[predicate selects](crud.md): repeat it per criterion, criteria combine in declaration order
(AND by default, `Or = true` to join with OR), and parameters bind positionally —
`Compare.Between` / `Compare.NotBetween` consume two, `Compare.In` / `Compare.NotIn` one collection
(expanded at run time; an empty `NotIn` matches every row), `IsNull`/`IsNotNull` none. The full operator
set is the comparisons (`Equal`, `NotEqual`, `GreaterThan`, …), `Like` / `NotLike` (string fields), `In`
/ `NotIn`, `Between` / `NotBetween`, and the null checks.

## Rules

- The method must return `Task<int>` (rows affected).
- **At least one `[InquiryWhere]` is required** — an unfiltered set-based mutation is almost
  always a bug; use `[InquiryUpdateAll]`/`[InquiryDeleteAll]` for whole-collection operations.
- `[InquiryUpdateWhere]`'s SET fields must be ordinary mutable columns: not the key, not
  database-generated, not the soft-delete indicator, not a concurrency token.
- **Concurrency-token entities are rejected** for both operations (same rationale as batch
  mutations: a set-based statement cannot check per-row tokens).

## Soft-delete entities

On a soft-delete entity, `[InquiryDeleteWhere]` emits the **soft UPDATE form** (sets the
indicator) and both operations compose the active-row filter into the WHERE — already-deleted
rows are never updated or re-deleted. `[InquiryDeleteWhere(HardDelete = true)]` forces a literal
`DELETE`, mirroring `[InquiryDeleteOneByKey]`.

## Transactions and timeouts

Like every generated method, set-based mutations participate in ambient Inquiry transactions and
honor `InquiryOptions.DefaultCommandTimeout`. A predicate mutation is a single statement, so it is
atomic on its own.
