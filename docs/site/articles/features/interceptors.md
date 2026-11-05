# Interceptors & the Inquiry.Interceptors package

`IInquiryCommandInterceptor` is the pipeline's cross-cutting seam: implementations see every command at four points — `CommandInitializedAsync` (text + parameters applied), `CommandExecutingAsync` (immediately before execution; the `DbCommand` is still mutable), `CommandExecutedAsync`, and `CommandFailedAsync`. Register any number as singleton `IInquiryCommandInterceptor` services; they run in registration order.

Multi-result commands (`QueryMultipleAsync`, generated eager loads, and generated `InquiryPagedResult<T>` methods) raise the same callbacks, inside or outside a transaction. `CommandExecutedAsync` runs when the `InquiryGridReader` is disposed, even if some result sets were not read. `CommandFailedAsync` runs instead, once, when execution or any grid read fails or is cancelled. The built-in telemetry interceptor is not called for grids, because the grid records its own `QUERY_MULTIPLE` span and duration metric.

The core ships the seam only. **`Inquiry.Interceptors`** is the opt-in companion package with ready-made implementations:

## Slow-query warning logging

```csharp
builder.Services.AddInquirySlowQueryLogging(TimeSpan.FromMilliseconds(500));   // default 1s
```

Logs a warning on the `ILogger<SlowQueryLoggingInterceptor>` category (the type's full name) whenever a command's **executing → executed window** meets the threshold. For queries that window covers the full command — provider execution *plus* result reading and materialization — which is usually what "this query is slow" means in practice:

> `Inquiry command took 1240 ms (threshold 500 ms): SELECT … FROM "Orders" WHERE …`

Command text is logged; **parameter values never are** — the same posture as Inquiry's [telemetry](observability.md).

## sqlcommenter query tagging

```csharp
builder.Services.AddInquirySqlCommenter("checkout-api");
```

Appends a [sqlcommenter](https://google.github.io/sqlcommenter/)-style comment to each statement:

```sql
SELECT … FROM "Orders" WHERE "OrderID" = @OrderID /*application='checkout-api',traceparent='00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01'*/
```

`traceparent` comes from `Activity.Current` (W3C format), so database-side tooling — slow-query logs, `pg_stat_activity`, DBA traces — correlates statements back to the exact distributed trace that issued them. With no current activity and no application name, text is left untouched; already-commented text is never double-tagged (retries are safe).

**Trade-off:** trace ids change per request, so tagged text varies per execution and defeats server-side [prepared-statement](prepared-statements.md) reuse for tagged commands. Enable it when DBA-side correlation matters more, or scope it to diagnosis sessions.

## N+1 detection

N+1 detection warns when the same SQL runs repeatedly inside one unit of work, such as a loop that
loads each customer's orders with a separate query. It needs two steps:

1. Register the interceptor with `AddInquiryNPlusOneDetection`.
2. Wrap the request, job or test you want to check in `InquiryNPlusOneScope.BeginScope()`.

Registration alone does nothing. The interceptor counts commands only while a scope is active.
Outside a scope it checks for a scope and returns, so you can leave it registered in production.

### Register

The example uses an order store with one query per customer. Create **Order.cs**:

<!-- recipe: n-plus-one/Order.cs -->
```csharp
using Inquiry.Entities;

[InquiryTable("Orders")]
public sealed class Order
{
    [InquiryKey("OrderID", IsGenerated = true)]
    public int? OrderID { get; set; }

    [InquiryColumn]
    public int CustomerID { get; set; }
}
```

Create **OrderStore.cs**:

<!-- recipe: n-plus-one/OrderStore.cs -->
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Stores;

public partial class OrderStore : InquiryStore<Order>
{
    [InquirySelectAllByField("CustomerID")]
    public partial Task<IReadOnlyList<Order>> ForCustomerAsync(
        int customerId, CancellationToken ct = default);
}
```

Then register Inquiry, logging and the detector, and activate a scope around the work. Create
**NPlusOneExample.cs**:

<!-- recipe: n-plus-one/NPlusOneExample.cs -->
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.DependencyInjection;
using Inquiry.Interceptors;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class NPlusOneExample
{
    public static IServiceCollection AddOrders(
        this IServiceCollection services,
        string connectionString,
        Action<ILoggingBuilder> configureLogging)
    {
        services.AddLogging(configureLogging);
        services.AddInquiry();
        services.AddInquiryGeneratedStores();
        services.AddInquirySqlite(connectionString);
        services.AddInquiryNPlusOneDetection(threshold: 3);
        return services;
    }

    public static async Task LoadOrdersAsync(
        OrderStore orders, IEnumerable<int> customerIds, CancellationToken ct = default)
    {
        foreach (var customerId in customerIds)
        {
            await orders.ForCustomerAsync(customerId, ct);
        }
    }

    public static async Task RunAsync(OrderStore orders, CancellationToken ct = default)
    {
        int[] customers = [1, 2, 3, 4];

        // No scope is active, so nothing is counted.
        await LoadOrdersAsync(orders, customers, ct);

        using (InquiryNPlusOneScope.BeginScope())
        {
            await orders.ForCustomerAsync(1, ct);

            using (InquiryNPlusOneScope.BeginScope())
            {
                // Warns on the third query. The outer scope does not see these four.
                await LoadOrdersAsync(orders, customers, ct);
            }

            // The outer scope counted one query before the nested scope, so customer 3 warns.
            await LoadOrdersAsync(orders, [2, 3, 4], ct);
        }

        // The scopes are disposed, so nothing is counted.
        await LoadOrdersAsync(orders, customers, ct);
    }
}
```

`RunAsync` logs two warnings: one in the nested scope and one in the outer scope. Each reads:

> `Possible N+1: the same SQL executed 3 times within this detection scope: SELECT … FROM "Orders" WHERE "CustomerID" = @CustomerID`

The warning goes to the `ILogger<NPlusOneDetectionInterceptor>` category. Command text is logged;
parameter values never are.

### Rules

- **Logging is required.** The detector resolves `ILogger<NPlusOneDetectionInterceptor>`. A generic
  host or `WebApplication` registers logging for you. A plain `ServiceCollection` must call
  `AddLogging`; without it, resolving the interceptor throws `InvalidOperationException`.
- **Threshold.** The threshold is the total number of executions of the same SQL in one scope,
  including the first. The default is 2, so the first repeat warns. The minimum is 2. A lower value
  throws `ArgumentOutOfRangeException` when the interceptor is first resolved, not when you register
  it.
- **One warning per statement per scope.** Executions after the threshold do not log again in that
  scope.
- **Same SQL, any parameter values.** The detector compares command text. Calls that differ only in
  parameter values, such as different customer IDs, count as the same statement. A trailing
  sqlcommenter comment is ignored.
- **Nesting.** A nested scope starts with zero counts. Queries inside it do not count toward the
  outer scope. When the nested scope is disposed, the outer scope becomes active again with its own
  counts.
- **Disposal.** Disposing a scope ends detection for it. Begin and dispose the scope in the same
  method, around the awaited work, with `using`. A scope begun inside an `async` helper method does
  not cover the caller: when the helper returns, the caller's async context no longer has that scope
  active.
- **Disposal order.** Dispose nested scopes innermost first; nested `using` blocks do this. Other
  orders leave the wrong scope active.

A warning means the same statement ran repeatedly. It does not prove a defect: some loops are
intended. Use the logged SQL to find the loop, then decide whether a batch query or
[eager loading](eager-loading.md) fits better.

## Writing your own

```csharp
public sealed class CountingInterceptor : IInquiryCommandInterceptor
{
    public int Executed;
    public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Executed);
        return ValueTask.CompletedTask;
    }
}

services.AddSingleton<IInquiryCommandInterceptor, CountingInterceptor>();
```

All four methods have default no-op implementations — override only what you need. For test assertions over executed commands, use the recording interceptor that ships in [`Inquiry.Testing`](testing.md).

## See also

- [Observability](observability.md) — spans, metrics, and logs (the telemetry interceptor).
- [Testing](testing.md) — the recording command interceptor with assertion helpers.
