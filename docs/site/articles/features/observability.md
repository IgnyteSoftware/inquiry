# Observability (OpenTelemetry, metrics, logging)

Inquiry ships an **opt-in** telemetry layer built on the command-interceptor seam. When it is not
registered, the pipeline carries **zero** telemetry overhead (the interceptor fast path skips every
notification); when it is registered but nothing is listening, each signal is a cheap no-op.

Enable it with one call:

```csharp
services
    .AddInquiry()
    .AddInquiryTelemetry()          // tracing + metrics + logging
    .AddInquirySqlite(connectionString);
```

## Tracing

Every command the pipeline executes becomes a `System.Diagnostics.Activity` (span) on the
**`"Inquiry"`** `ActivitySource`, following the OpenTelemetry database semantic conventions:

| Span data | Value |
|---|---|
| Name / kind | leading SQL keyword (`SELECT`, `INSERT`, …) / `Client` |
| `db.system.name` | `sqlite`, `microsoft.sql_server`, `postgresql`, `mysql`, `oracle.db` (derived from the ADO.NET command type — no per-provider wiring) |
| `db.operation.name` | leading SQL keyword |
| `db.query.text` | the executed SQL (see [Redaction](#redaction)) |
| `db.response.affected_rows` | row count for non-queries |
| `error.type` + span status `Error` | exception type on failure |

Subscribe an OpenTelemetry tracer to the source:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(InquiryTelemetry.ActivitySourceName));
```

Spans parent automatically to the ambient `Activity` (e.g. the ASP.NET Core request span). A span
covers execution **through result-set consumption**; a streaming query whose enumeration is
abandoned early is dropped rather than recorded with a fabricated duration.

## Metrics

The **`"Inquiry"`** `Meter` publishes the OpenTelemetry-conventional
**`db.client.operation.duration`** histogram (seconds), tagged with `db.system.name` and
`db.operation.name`; failed commands additionally carry `error.type`, so throughput, latency
percentiles, and error rate all derive from the one instrument:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(InquiryTelemetry.MeterName));
```

## Logging

When an `ILoggerFactory` is registered, the **`Inquiry.Command`** category logs:

- `Debug` — command executing (operation + SQL) and executed (elapsed ms + rows affected),
- `Error` — command failed (elapsed ms, with the exception).

Messages use cached `LoggerMessage` delegates, so disabled levels cost a single `IsEnabled` check.

## Redaction

Inquiry SQL is built at **compile time** from constant templates: values flow only through bound
parameters, and **parameter values are never recorded** on spans, metrics, or logs. If even the SQL
shape (table/column names) must not leave the process, turn the text off:

```csharp
services.AddInquiryTelemetry(o => o.RecordCommandText = false);
```

## Health checks

The core package ships an ASP.NET Core health check that opens a connection through the
registered connection factory — the same open path the pipeline uses, including configured retry
and failover:

```csharp
builder.Services.AddHealthChecks().AddInquiry();   // name "inquiry", Unhealthy on failure
app.MapHealthChecks("/health");
```

`AddInquiry(name, failureStatus, tags)` overload parameters integrate with readiness/liveness
endpoint filtering.

## Custom interceptors

The telemetry layer is an ordinary [`IInquiryCommandInterceptor`](../architecture.md); register your
own implementations alongside (or instead of) it for bespoke auditing, tagging, or APM integrations.
