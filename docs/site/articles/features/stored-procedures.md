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
| `Task<TScalar>` + `OutputParameter`/`ReturnsValue` | Read-back scalar (see below) |

## OUTPUT parameters and RETURN values

To surface a single value a procedure produces — through an `OUTPUT` parameter or its integer `RETURN` value — declare the method as `Task<TScalar>` and set one of the two knobs. The read-back value becomes the task result; the other method parameters are still the IN parameters.

```csharp
public partial class OrderStore : InquiryStore<Order>
{
    // OUTPUT parameter: @Total is read back as the decimal result.
    [InquiryStoredProcedure("usp_SumByCategory", OutputParameter = "Total")]
    public partial Task<decimal> SumByCategoryAsync(string category, CancellationToken ct = default);

    // RETURN value: the procedure's integer RETURN is the result.
    [InquiryStoredProcedure("usp_CountByCategory", ReturnsValue = true)]
    public partial Task<int> CountByCategoryAsync(string category, CancellationToken ct = default);
}
```

- The generator binds the named parameter with `ParameterDirection.Output` (stamping its `DbType`, and `Size = -1` for `string`), or a `ParameterDirection.ReturnValue` parameter for `ReturnsValue`, then reads it back after execution.
- A RETURN value is always an integer, so `ReturnsValue = true` requires `Task<int>`. `OutputParameter` and `ReturnsValue` are mutually exclusive. Misconfiguration is a build error (`INQ051`).
- This scalar-output form doesn't also map a result set — use a separate method for rows. Use `Task<TScalar?>` when the OUTPUT can be `NULL`.

## Limitations (today)

- **INOUT parameters** aren't surfaced — an OUTPUT parameter is read back, but a value passed *in* and mutated is not returned to the caller.
- **No multiple result sets.** Only the first rowset is materialized.
- **No table-valued parameters.** Pass scalars or a comma-joined string.
- **Oracle limitation:** SPs that return rows require an `OUT REF CURSOR` parameter, which the generator doesn't yet emit. Use a function with `RETURN SYS_REFCURSOR` as a workaround.
