# Interceptors & the Inquiry.Interceptors package

`IInquiryCommandInterceptor` is the pipeline's cross-cutting seam: implementations see every command at four points — `CommandInitializedAsync` (text + parameters applied), `CommandExecutingAsync` (immediately before execution; the `DbCommand` is still mutable), `CommandExecutedAsync`, and `CommandFailedAsync`. Register any number as singleton `IInquiryCommandInterceptor` services; they run in registration order.

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

## N+1 query detection

A dev-time detector (Rails [bullet](https://github.com/flyerhzm/bullet) / [prosopite](https://github.com/charkost/prosopite) analog) that flags the classic N+1: one parent query followed by N child queries with the same SQL and different parameters.

```csharp
builder.Services.AddInquiryNPlusOneDetection(threshold: 2);   // warn at the 2nd repeat (default)
```

Detection is **scoped** — wrap a logical unit of work (an HTTP request, a job, a test) and the detector counts how often each distinct command text runs *within that scope*, warning once a statement reaches the threshold:

```csharp
using (InquiryNPlusOneScope.BeginScope())
{
    var orders = await orderStore.RecentAsync();
    foreach (var order in orders)
        order.Customer = await customerStore.ByIdAsync(order.CustomerId);   // same SQL each iteration
}
// Warning: Possible N+1: the same SQL executed 2 times within this detection scope … : SELECT … FROM "Customers" WHERE "CustomerID" = @CustomerID
```

Because Inquiry parameterizes values, the per-iteration command text is identical, so the repeats fingerprint together. Outside any scope the detector is a no-op (zero overhead in production paths you don't wrap). The fix is usually [eager loading](eager-loading.md) or a single [`In`](crud.md) query. Command text is logged; parameter values never are. Intended for development and test runs, not production hot loops.

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
