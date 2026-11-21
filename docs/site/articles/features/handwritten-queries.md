# Handwritten SQL

Inquiry runs SQL that you write in two ways:

- `[InquiryQuery]` on a store method. The SQL is a constant, each `@name` binds to the method
  parameter of that name, and the build checks the binding.
- The `IInquiry` query methods with interpolated SQL. Each interpolation hole becomes a bound
  parameter, and the SQL is built at the call.

Both map result columns to properties by name, as [ad-hoc DTOs](ad-hoc-dtos.md) describe.

## Choosing one

| | `[InquiryQuery]` store method | `IInquiry` with interpolated SQL |
|---|---|---|
| SQL text | A compile-time constant. `const` fragments can be concatenated. | Built at the call. |
| Parameters | Named. An unknown or unused name is build error `INQ123`. | One per interpolation hole. |
| Call site | A typed store method. With `[InquiryGenerateInterface]` it is also on the generated store interface, so tests can replace it. | Any code that has an `IInquiry`. |
| Results | A list, one row or `null`, a scalar, or an affected-row count. | Also streaming (`QueryAsync<T>`) and multiple result sets. |

Use `[InquiryQuery]` when the SQL is fixed: reports, CTE queries and statements that the generated
operations do not express. The SQL then sits next to the other store methods and the build checks
its parameters. Use `IInquiry` when the SQL changes at run time, when you stream rows or read several
result sets, or for a one-off statement outside a store.

## Declaring a query

```csharp
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryAdHoc]
public sealed class ActivitySummaryRow
{
    public int MemberKey { get; set; }
    public DateTime LastCheckIn { get; set; }
    public int Visits { get; set; }
}

public partial class ActivityStore : InquiryStore<Activity>
{
    // One CTE, shared by the page query and its count.
    private const string RecentActivity = """
        WITH recent AS (
            SELECT [TMemberKey], [CheckInDateTime]
            FROM [Activity]
            WHERE [TMemberKey] = @memberKey AND [CheckInDateTime] >= @startUtc)
        """;

    [InquiryQuery(RecentActivity + """
        SELECT [TMemberKey] AS MemberKey, MAX([CheckInDateTime]) AS LastCheckIn, COUNT(*) AS Visits
        FROM recent
        GROUP BY [TMemberKey]
        """)]
    public partial Task<IReadOnlyList<ActivitySummaryRow>> QueryAsync(
        int memberKey, DateTime startUtc, CancellationToken cancellationToken = default);

    [InquiryQuery(RecentActivity + "SELECT COUNT(*) FROM recent")]
    public partial Task<long> CountAsync(
        int memberKey, DateTime startUtc, CancellationToken cancellationToken = default);
}
```

The same query through `IInquiry` puts the values in interpolation holes:

```csharp
var rows = await inquiry.QueryListAsync<ActivitySummaryRow>($"""
    SELECT [TMemberKey] AS MemberKey, MAX([CheckInDateTime]) AS LastCheckIn, COUNT(*) AS Visits
    FROM [Activity]
    WHERE [TMemberKey] = {memberKey} AND [CheckInDateTime] >= {startUtc}
    GROUP BY [TMemberKey]
    """, cancellationToken);
```

To move a `FormattableStringFactory.Create(sql, args)` call with `{0}` placeholders to a store
method, replace each placeholder with `@name` and declare a parameter of that name.

## Parameters

- Each `@name` binds to the method parameter with exactly that name, including case. A name with
  no parameter, and a parameter that the SQL does not use, are build error `INQ123`. One parameter
  can appear more than once.
- Two parameters whose names differ only in case cannot both appear in the SQL, because providers
  match parameter names without case.
- The generator does not read names inside string literals, quoted identifiers or comments. A
  prefix that follows a letter, a digit, a closing identifier quote or the same prefix is not a
  parameter, so SQL Server `@@ROWCOUNT`, PostgreSQL `value::int` and an Oracle `table@link` or
  `"Order Details"@link` stay as written.
- Parameters are scalar values: numbers, strings, `bool`, `Guid`, dates and times, `byte[]` and enums.
  `[InquiryParameter(Length = …, Precision = …, Scale = …, IsUnicode = …)]` sets the parameter metadata.
  Collections are not supported.
- The trailing `CancellationToken` is required and is not a SQL parameter.

Write parameters with `@` on every dialect. The generated command uses the dialect's own form.

| Dialect | Accepted prefixes | Also skipped by the scan |
|---|---|---|
| SQL Server | `@` | `[bracketed]` identifiers, nested comments |
| PostgreSQL | `@` | `$tag$` strings, `E'…'` strings, nested comments |
| MySQL, MariaDB | `@` | `` `backtick` `` identifiers, `#` comments, backslash escapes. `--` starts a comment only before a space. |
| Oracle | `:` and `@` | `q'[…]'` and `nq'[…]'` strings |
| SQLite | `@`, `:` and `$` | `[bracketed]` and `` `backtick` `` identifiers |

Every dialect also skips `'…'` strings, `"…"` identifiers, `--` comments and `/* … */` comments.

Because every `@name` must be a method parameter, SQL that declares SQL Server local variables
(`DECLARE @total int`) or reads MySQL user variables (`@rank`) reports `INQ123`. Run such SQL through
`IInquiry`.

## Return types

| Return type | Result |
|---|---|
| `Task<IReadOnlyList<T>>` | Every row. An empty result is an empty list. |
| `Task<T?>` | The only row, or `null`. More than one row throws `InvalidOperationException`. |
| `Task<int>` | The affected-row count from the provider. |
| `Task<TScalar>` | The first column of the first row, converted to `TScalar`. No row or SQL `NULL` gives `default`. `TScalar` is `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`, `bool`, `string` or `DateTime`, or a nullable one. For any other single value, such as a `Guid`, an enum or an unsigned number, return a one-property `[InquiryAdHoc]` row. |

`T` is an `[InquiryTable]`, `[InquiryView]`, `[InquiryProjection]` or `[InquiryAdHoc]` class in the
same assembly as the store. Entity and projection rows match their mapped column names, such as an
`[InquiryColumn]` name. Ad-hoc rows match property names. A missing column throws before the first row is returned.

To read an `int` scalar instead of the row count, set `Scalar = true`:
`[InquiryQuery("SELECT COUNT(*) FROM [Activity]", Scalar = true)] Task<int> CountAsync(...)`.

## Transactions and policies

A query method runs in the active Inquiry transaction, such as one started by
`ExecuteInTransactionAsync`, like every generated store method.

Handwritten SQL is outside generated policy. [Global filters](global-filters.md), soft-delete
filters, tenant predicates and audit values do not apply, so write those conditions in the SQL. A
[view entity](view-entities.md) store accepts query methods, except a `Task<int>` row count, which
reports `INQ052`.

## See also

- [Ad-hoc DTOs](ad-hoc-dtos.md) for result mapping.
- [Query composition](query-composition.md#when-sql-is-clearer) for when SQL is clearer than attributes.
- [Stored procedures](stored-procedures.md) for SQL that lives in the database.
- [Security](../security.md) for how parameters are bound.
