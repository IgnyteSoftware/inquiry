# Prepared-Statement Reuse (W4)

Inquiry bakes every statement as a stable `const string`, which makes it a natural fit for
`DbCommand.Prepare()` / server-side prepared statements. W4 adds **opt-in, capability-gated**
preparation, plus the parameter-type metadata (`DbType`) that makes preparation effective.

## Enabling it

```csharp
services.AddInquiry(options =>
{
    options.PrepareStatements = PreparedStatementMode.Auto; // None (default) | Auto
});
```

- `PreparedStatementMode.None` (**default**) — today's behavior; no `Prepare()` call.
- `PreparedStatementMode.Auto` — the pipeline calls `PrepareAsync()` before execution, but **only**
  on providers whose connections retain prepared state across the pooled-connection lifecycle, and
  never for `CommandType.StoredProcedure`.

It is **off by default** deliberately: the win depends on the connection lifecycle (see below), and
explicit per-command `Prepare()` is net-negative on providers where prepared state dies with the
logical connection.

## Per-provider capability

`IInquiryConnectionFactory.SupportsPersistentPreparedStatements` gates whether `Auto` actually calls
`PrepareAsync()`:

| Provider | Capability | Why |
| --- | :--: | --- |
| PostgreSQL (Npgsql) | **true** | Prepared statements are cached on the **pooled physical connection** and survive the logical connection being disposed, so repeated `Prepare()` of the same `const` SQL is a cheap cache hit. |
| SQL Server (Microsoft.Data.SqlClient) | false | `sp_prepare` handles are scoped to the open connection and lost on dispose; the server already caches plans by parameterized text, so per-command `Prepare()` is pure overhead. Rely on the plan cache. |
| SQLite (Microsoft.Data.Sqlite) | false | In-process compile tied to the connection; Inquiry opens a connection per operation, which negates reuse. |
| MySQL (MySqlConnector) | false | (Same per-operation-connection rationale; revisit if connection retention is added.) |

### Npgsql: prefer the connection-string path

For PostgreSQL the **recommended production setup** is Npgsql's automatic preparation via the
connection string, which transparently prepares statements once they cross a usage threshold:

```
Host=...;Database=...;Max Auto Prepare=20;Auto Prepare Min Usages=2
```

Inquiry's per-command `Prepare()` (`Auto`) is the cross-provider fallback; `Max Auto Prepare` is the
lower-overhead option when you control the connection string.

## Parameter `DbType` metadata (F6)

Independently of the mode, the generator now emits an explicit `DbType` on each generated parameter
(via `DbTypeMapper`, mapping the compile-time `TypeData`). This is what gives `Prepare()` fixed
parameter types so it does not re-infer (and invalidate) on every call. Types with no portable
`DbType` (e.g. `byte[]`, custom converters) emit no assignment and fall back to provider inference.

> **Note — `System.DateTime` maps to `DbType.DateTime2`, not `DbType.DateTime`.** An explicit `DbType`
> is emitted even when preparation is off, and SqlClient maps `DbType.DateTime` to the legacy
> `datetime` SQL type (range 1753+, ~3.33 ms precision), which can truncate/throw against modern
> `datetime2` columns. `DbType.DateTime2` round-trips both and is the modern default; Npgsql/SQLite/
> MySQL treat them equivalently.

## Caveats

- **`Compare.In` predicates (W1) are not prepared-reuse-friendly.** Variadic `IN` expansion rewrites
  the command text per cardinality, so the text is no longer constant and a prepared statement does
  not get reused across different list lengths. This is a correctness non-issue (the rewritten text
  still prepares fine for that one execution) but a perf non-win; a future option is array parameters
  (`= ANY(@ids)` on PostgreSQL) which keep the SQL constant.
- **Connection lifecycle is the crux.** Inquiry opens/disposes a connection per operation, so only
  Npgsql's pool-level prepared-statement cache and the *transacted* pipeline (which holds one
  connection across the transaction) see real reuse.

## Status

A BenchmarkDotNet comparison (`None` vs `Auto` on Npgsql, simple + multi-join queries) is tracked as a
follow-up to quantify the win.
