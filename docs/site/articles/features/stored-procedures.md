# Stored procedures

Call a stored procedure from an abstract store method by attaching `[InquiryStoredProcedure("usp_Name")]`. The method's parameters become the procedure's IN parameters (with `@`-prefix naming, rewritten to `:name` on Oracle by the connection factory).

## You write

```csharp
public partial class CustomerStore : InquiryStore<Customer>
{
    [InquiryStoredProcedure("usp_GetCustomersByCountry")]
    public partial Task<IReadOnlyList<Customer>> GetByCountryAsync(string country, CancellationToken ct = default);

    [InquiryStoredProcedure("usp_GetCustomerByID")]
    public partial Task<Customer?> GetByIDAsync(string id, CancellationToken ct = default);

    [InquiryStoredProcedure("usp_PurgeInactive")]
    public partial Task<int> PurgeInactiveAsync(int olderThanDays, CancellationToken ct = default);
}
```

## Supported return shapes

| Return type | Pipeline call |
|---|---|
| `IAsyncEnumerable<TEntity>` | Streaming rows |
| `Task<TEntity?>` | Single row |
| `Task<int>` | Records affected |

## Limitations (today)

- **No OUT / INOUT parameters yet.** Method params bind as IN only.
- **No scalar return** (`Task<decimal>` etc.) — use the records-affected `Task<int>` or wrap with `[InquiryAggregate]` against a `SELECT` SP.
- **No multiple result sets.** Only the first rowset is materialized.
- **No table-valued parameters.** Pass scalars or a comma-joined string.
- **Oracle limitation:** SPs that return rows require an `OUT REF CURSOR` parameter, which the generator doesn't yet emit. Use a function with `RETURN SYS_REFCURSOR` as a workaround until OUT-param support lands.

A future "stored procedure expansion" release will add OUT/INOUT, scalar returns, multi-result-set, and Oracle ref cursor support. See the project status doc for the current plan.
