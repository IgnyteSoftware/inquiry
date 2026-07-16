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
| `Task<TScalar>` + `[InquiryParameter(IsInputOutput = true)]` | INOUT read-back (see below) |
| `Task<(IReadOnlyList<A>, IReadOnlyList<B>, …)>` | Multiple typed result sets (see below) |

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

## INOUT parameters

Use `[InquiryParameter(IsInputOutput = true)]` to mark a parameter as `ParameterDirection.InputOutput` — the value is passed to the procedure and the modified value is read back as the `Task<T>` result.

```csharp
public partial class CounterStore : InquiryStore<Counter>
{
    [InquiryStoredProcedure("usp_Increment")]
    public partial Task<int> IncrementAsync(
        [InquiryParameter(IsInputOutput = true)] int counter,
        CancellationToken ct = default);
}
```

- At most one INOUT parameter per method. Its CLR type must match the return type.
- Mutually exclusive with `OutputParameter` and `ReturnsValue`. Misconfiguration is a build error (`INQ051`).
- String and byte-array INOUT parameters auto-size to `Size = -1` (MAX). Decimal parameters stamp `Precision = 38; Scale = 10` by default; override with `[InquiryParameter(Precision = …, Scale = …)]`.

## Multiple result sets

Return `Task<(IReadOnlyList<A>, IReadOnlyList<B>, …)>` to surface multiple typed result sets from a single stored procedure. Each tuple element maps to one result set in order.

```csharp
public partial class ReportStore : InquiryStore<Order>
{
    [InquiryStoredProcedure("usp_GetOrderReport")]
    public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetReportAsync(
        long customerId, CancellationToken ct = default);
}
```

- Each tuple element must be `IReadOnlyList<TEntity>` where `TEntity` is a mapped `[InquiryTable]` entity.
- Mutually exclusive with `OutputParameter`, `ReturnsValue`, and INOUT.
- The generated code uses `QueryMultipleAsync` and `InquiryGridReader.ReadListAsync` per result set.

## Oracle stored procedures

Oracle stored procedures cannot return result sets directly — they require `OUT SYS_REFCURSOR` parameters. Inquiry handles this transparently: entity-returning stored procedure calls on the Oracle provider are wrapped in a PL/SQL block that declares local cursor variables, passes them to the procedure, and surfaces them through `DBMS_SQL.RETURN_RESULT`.

The user's Oracle procedure must declare the `OUT SYS_REFCURSOR` parameter(s) **after** all input parameters:

```sql
CREATE PROCEDURE GET_EMPLOYEES_BY_DEPT(
    p_dept_id IN NUMBER,
    p_cursor  OUT SYS_REFCURSOR)
AS BEGIN
    OPEN p_cursor FOR SELECT * FROM Employee WHERE DeptId = p_dept_id;
END;
```

The C# declaration is the same as any other provider — no mention of the cursor parameter:

```csharp
[InquiryStoredProcedure("GET_EMPLOYEES_BY_DEPT")]
public partial IAsyncEnumerable<Employee> GetByDeptAsync(long deptId, CancellationToken ct = default);
```

For multi-result stored procedures, declare one `OUT SYS_REFCURSOR` per result set, in the same order as the tuple elements.

> **Input parameter order matters.** The PL/SQL wrapper passes arguments positionally, so your procedure's IN parameters must appear in the same order as the C# method parameters. Their names do not need to match.

> **Every cursor must be opened.** If a procedure conditionally skips opening a cursor, `DBMS_SQL.RETURN_RESULT` raises ORA-29478 at runtime. Ensure every declared `OUT SYS_REFCURSOR` is opened on every code path (open an empty cursor with `OPEN p_cursor FOR SELECT … WHERE 1 = 0` for the no-data case).

## Limitations (today)

- **No table-valued parameters in stored procedure calls.** TVPs are used internally for SQL Server `Compare.In` and `[InquiryDeleteAll]` collections, but stored procedure methods cannot yet accept TVP parameters directly.
