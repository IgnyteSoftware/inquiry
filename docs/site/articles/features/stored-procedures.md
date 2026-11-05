# Stored procedures

Call a stored procedure from a partial store method by attaching `[InquiryStoredProcedure("usp_Name")]`. The method's parameters, except the trailing `CancellationToken`, become the procedure's IN parameters. Their database names come from the C# parameter names.

## You write

```csharp
public partial class CustomerStore : InquiryStore<Customer>
{
    [InquiryStoredProcedure("usp_GetCustomersByCountry")]
    public partial Task<IReadOnlyList<Customer>> GetByCountryAsync(string country, CancellationToken ct = default);

    [InquiryStoredProcedure("usp_GetCustomersByRegion")]
    public partial IAsyncEnumerable<Customer> StreamByRegionAsync(string region, CancellationToken ct = default);

    [InquiryStoredProcedure("usp_GetCustomerByID")]
    public partial Task<Customer?> GetByIDAsync(string id, CancellationToken ct = default);

    [InquiryStoredProcedure("usp_PurgeInactive")]
    public partial Task<int> PurgeInactiveAsync(int olderThanDays, CancellationToken ct = default);
}
```

## Provider support

Inquiry generates `[InquiryStoredProcedure]` methods for SQL Server and Oracle only. On SQLite,
PostgreSQL, MySQL and MariaDB, a procedure method fails the build with [INQ114](../diagnostics.md#inq114).
SQLite has no stored procedures. PostgreSQL, MySQL and MariaDB have them, but this repository has no
live procedure tests for those providers, so Inquiry does not generate procedure calls for them yet.

| Feature | SQL Server | Oracle |
|---|---|---|
| Buffered list, stream, single row | Yes | Yes, through a `SYS_REFCURSOR` wrapper |
| `Task<int>` without a read-back | Yes: the driver's affected-row count, or -1 under `SET NOCOUNT ON` | Yes: always -1 |
| `OutputParameter` | Yes | Numbers only; other types report INQ114 |
| INOUT parameter | Yes | Numbers, and strings or `byte[]` with a `Length`; other types report INQ114 |
| `ReturnsValue` (RETURN status) | Yes | No: INQ114 |
| Multiple result sets | Yes | Yes, one `SYS_REFCURSOR` per set |
| Table-valued parameters | Yes | No: INQ086 |

Each "Yes" has a live test in `tests/Inquiry.SqlServer.Tests` or `tests/Inquiry.Oracle.Tests`
(`StoredProcedureProviderSupportIntegrationTests` and the procedure list and TVP tests). The tests
check that each call runs the procedure once, that a call inside `ExecuteInTransactionAsync` commits
and rolls back with the transaction, and that a canceled token stops the call before the procedure runs.

On Oracle:

- Procedures have no RETURN status. Declare an `OUT NUMBER` parameter and set `OutputParameter`.
- ODP.NET needs a buffer size for string and binary output, and `OutputParameter` cannot declare one.
  Return a string or `byte[]` through an INOUT parameter with
  `[InquiryParameter(IsInputOutput = true, Length = n)]`, where `n` is the size the procedure declares.
- Guid, DateTime and bool values cannot be read back from a procedure. Return them in a result set.
  Guid and DateTime IN parameters also fail in ODP.NET today, because procedure parameters do not use
  Oracle's type mapping.
- Methods that read back a value or return `Task<int>` bind arguments by name, so the C# parameter
  names must match the procedure's parameter names. Row-returning methods pass arguments by position
  (see [Oracle stored procedures](#oracle-stored-procedures)).

## Supported return shapes

| Return type | Pipeline call |
|---|---|
| `Task<IReadOnlyList<TEntity>>` | Buffered rows; empty list when the procedure returns none |
| `IAsyncEnumerable<TEntity>` | Streaming rows |
| `Task<TEntity?>` | Zero or one row; null when empty, exception on multiple rows |
| `Task<int>` without a read-back option | Provider records-affected result; may be -1 |
| `Task<TScalar>` + `OutputParameter`/`ReturnsValue` | Read-back scalar (see below) |
| `Task<TScalar>` + `[InquiryParameter(IsInputOutput = true)]` | INOUT read-back (see below) |
| `Task<(IReadOnlyList<A>, IReadOnlyList<B>, …)>` | Multiple typed result sets (see below) |

`Task<IReadOnlyList<TEntity>>` reads every row of the procedure's result before the task completes,
like an ordinary buffered select. The buffered list works with the Oracle cursor wrapper and with SQL
Server TVP parameters. Use `IAsyncEnumerable<TEntity>` to process large results one row at a time.

SELECT results, affected rows, RETURN status, and OUTPUT values are different channels. For example,
SQL Server's NOCOUNT behavior can make the affected-row result -1. Neither a positive count nor a
RETURN integer has a universal business-success meaning; follow the procedure's contract.

A scalar OUTPUT/INOUT database NULL becomes default(T); use a nullable scalar when NULL must remain
distinct from zero. Entity-returning calls use single-or-default cardinality, not first-row selection.

Each call executes the procedure once. A streaming method executes it once per enumeration: creating
the stream does not run the procedure, and enumerating the same stream again runs it again. Do not invoke a side-effecting procedure repeatedly
through different declarations to obtain its rows and outputs. The generated scalar read-back shape
does not also return SELECT rows. If one execution must expose both channels, use a caller-owned
ADO.NET command/reader or redesign the procedure to expose a supported single result shape.
See [Errors and partial outcomes](../error-contracts.md) for streaming and disposal failures.

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
- A RETURN value is always an integer, so `ReturnsValue = true` requires `Task<int>`. RETURN status is SQL Server only; Oracle reports `INQ114`. `OutputParameter` and `ReturnsValue` are mutually exclusive. Misconfiguration is a build error (`INQ051`).
- This scalar-output form does not also map a result set. A separate declaration is an alternative invocation, not a way to retrieve rows from the same execution. Use `Task<TScalar?>` when the OUTPUT can be `NULL`.

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
- String and byte-array INOUT parameters auto-size to `Size = -1` (MAX). Oracle does not accept that size, so on Oracle they need a positive `Length` (`INQ114` otherwise). Decimal parameters stamp `Precision = 38; Scale = 10` by default; override with `[InquiryParameter(Precision = …, Scale = …)]`.

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

> **Every cursor must be opened.** If a procedure conditionally skips opening a cursor, the call fails with ORA-01001 (invalid cursor). Ensure every declared `OUT SYS_REFCURSOR` is opened on every code path (open an empty cursor with `OPEN p_cursor FOR SELECT … WHERE 1 = 0` for the no-data case).

## Table-valued parameters on stored procedures

SQL Server stored procedures can accept `IEnumerable<T>` parameters as TVPs. Annotate the parameter with `[InquiryParameter(TvpTypeName = "[schema].[TypeName]")]` to specify the user-defined table type:

```csharp
[InquiryStoredProcedure("usp_BulkLookup")]
public partial Task<int> BulkLookupAsync(
    [InquiryParameter(TvpTypeName = "[dbo].[IntList]")] IEnumerable<int> ids,
    CancellationToken cancellationToken = default);
```

The generator resolves the element type's physical SQL Server mapping at compile time and emits an `InquiryTvpParameter.Bind` call with a cached `InquiryTvpDescriptor`. The user-defined type must already exist on the target database — the generator does not emit DDL for procedure TVP types (unlike `Compare.In` collections, whose types are fully managed).

Omitting `TvpTypeName` on a collection parameter reports `INQ086`. Non-SQL Server providers do not currently support TVP parameters on stored procedures.

## Result and output behavior

On both providers:

- An empty result gives an empty list or stream, and null for `Task<TEntity?>`. More than one row for
  `Task<TEntity?>` throws `InvalidOperationException`.
- Rows map by column name, as described under Limitations. Column order does not matter, and extra
  columns are ignored. A missing mapped column throws `InvalidOperationException` before the first row,
  and the message lists the result's columns. A value of an incompatible type throws when its row is read.
- A NULL OUTPUT or INOUT value becomes `default(T)`. Declare a nullable result, such as `Task<int?>`,
  when NULL must stay distinct from zero.
- A tuple reads one result set per element. On SQL Server, result sets after the last element are
  not read. On Oracle, the number of cursors must match exactly (see below).
- A canceled call, or one that fails while rows are read, does not show that the procedure wrote
  nothing. Call it inside `ExecuteInTransactionAsync` when its writes must roll back with the failure.

On SQL Server:

- A single-set method reads the first result set. A procedure that returns no result set gives an
  empty list or null.
- A procedure that returns fewer result sets than the tuple has elements throws
  `InvalidOperationException` ("no more result sets").

On Oracle:

- The wrapper passes exactly one `SYS_REFCURSOR` per result set. A procedure that declares fewer or
  more `OUT SYS_REFCURSOR` parameters than the method has result sets fails with PLS-00306 (wrong
  number or types of arguments). An extra cursor parameter is not ignored.
- A cursor that the procedure does not open fails with ORA-01001 (invalid cursor).

## Limitations (today)

- **Row types.** A single result set maps to the store's entity type. Each tuple element must be a
  mapped entity; otherwise the method reports `INQ051`. `[InquiryAdHoc]` DTOs are not accepted as
  procedure rows. To read a procedure into a DTO today, call it through `IInquiry` with an
  `InquiryCommand` that uses `CommandType.StoredProcedure`.
- **Column names.** Rows map by column name, not by position. Each entity property reads the result
  column with its mapped name (from `[InquiryColumn]` or `[InquiryKey]`), compared without regard to
  case. A column named after the property does not count when the mapped name differs. Extra columns
  are ignored. A missing column throws `InvalidOperationException` before the first row is returned,
  and the message lists the procedure's result columns. The same rules apply to a procedure called
  through `IInquiry` into an `[InquiryAdHoc]` DTO, which matches property names.
- **One channel per method.** A method returns rows, an affected-row count, or one scalar read-back
  (OUTPUT, RETURN, or INOUT). Rows cannot combine with a read-back. For example, a
  `Task<IReadOnlyList<TEntity>>` method with `OutputParameter`, `ReturnsValue` or an INOUT parameter
  reports `INQ051`.
- **Tuples.** A tuple needs at least two result sets. Use `Task<IReadOnlyList<TEntity>>` for one.
- **Parameter names.** Database parameter names come from the C# names. No attribute overrides
  them. The Oracle result-set wrapper passes arguments by position.
- **TVPs.** Table-valued parameters work on SQL Server only (`INQ086` on Oracle).
- **Providers.** Procedures are generated for SQL Server and Oracle only (`INQ114` elsewhere). See
  [Provider support](#provider-support).
- **Deployment.** Inquiry does not create or validate procedures. Deploy them with your migrations.
  A changed result schema shows up when rows are read.

The [1.0 decision record](../../develop/api-contract-decisions.md#423-typed-procedure-results-and-parameters)
describes the planned additions: DTO rows, combined results, and parameter-name overrides.
