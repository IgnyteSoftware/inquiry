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
| `db.collection.name` | target table, when the statement shape names one clearly (see [Redaction](#redaction)) |
| `db.response.affected_rows` | row count for non-queries |
| `error.type` + span status `Error` | exception type on failure; the status description is the exception message (see [Redaction](#redaction)) |
| `inquiry.error.cause.type` | provider exception type kept inside a cancellation (see [Cancellation](#cancellation)) |

`db.collection.name` is the last part of the target name in `INSERT [OR …] INTO t`,
`INSERT IGNORE INTO t`, `UPDATE [OR …] t`, `UPDATE TOP (n) t`, `DELETE FROM t`, and the first `FROM t`
of the outer `SELECT`. Bracketed, double-quoted, and backtick-quoted names, escaped delimiters, and
two- or three-part names are supported. Other statements, derived tables, and table-valued functions
get no tag rather than a wrong one.

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
shape (table/column names) must not leave the process, turn SQL details off:

```csharp
services.AddInquiryTelemetry(o => o.RecordCommandText = false);
```

With `RecordCommandText = false`, command, grid, batch, bulk-insert, and transaction spans and logs
keep:

- `db.system.name`, `db.operation.name`, durations, and row counts;
- `error.type` and `inquiry.error.cause.type` (exception type names).

They drop:

- `db.query.text` and the debug-log SQL;
- `db.collection.name`;
- the span status description, because provider error messages can quote table, column, and
  constraint names;
- the exception on the `Error` log. The message records the exception type instead:
  `Failed INSERT after 1.2 ms with Microsoft.Data.Sqlite.SqliteException`.

Your own code still receives the full exception. Grid, batch, and bulk-insert spans follow the setting
registered by `AddInquiryTelemetry()`; without that call they keep the default (`true`).

## Cancellation

When the caller's token fires while a command is in flight, Inquiry throws
`OperationCanceledException` even if the driver reported its own error. The driver error stays as the
`InnerException`. Telemetry records `error.type` as `System.OperationCanceledException`, so error rates
count the operation as cancelled, and adds `inquiry.error.cause.type` with the driver exception type.
When SQL details are recorded, the span status description contains both messages, and the `Error`
log carries the whole exception. When they are redacted, the log message names both types:
`… with System.OperationCanceledException caused by Microsoft.Data.SqlClient.SqlException`.

Cancellation never causes a statement retry. Connection-open retry policy applies only to opening
connections.

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
