# Prepared statements

Inquiry bakes every statement as a stable `const string`, which makes it a natural fit for server-side prepared statements. Preparation is **opt-in** and **capability-gated**.

## Wiring

```csharp
services.AddInquiryPostgreSql(connectionString, options =>
{
    options.PrepareStatements = PreparedStatementMode.Auto; // None (default) | Auto
});
```

- `PreparedStatementMode.None` (**default**) — no `Prepare()` call.
- `PreparedStatementMode.Auto` — the pipeline calls `PrepareAsync` once before execution, but **only** on providers whose connections retain prepared state across the pooled-connection lifecycle, and never for `CommandType.StoredProcedure`.

It is off by default deliberately: the win depends on the connection lifecycle (see [Caveats](#caveats)), and per-command `Prepare()` is net-negative on providers where prepared state dies with the logical connection.

## Provider support

`IInquiryConnectionFactory.SupportsPersistentPreparedStatements` gates whether `Auto` actually calls `PrepareAsync`. **Only PostgreSQL (Npgsql) returns `true` today**; the rest inherit the `false` default.

| Dialect | Persistent prepared-statement cache? | Why |
|---|:--:|---|
| PostgreSQL (Npgsql) | ✅ | Prepared statements are cached on the **pooled physical connection** and survive the logical connection being disposed, so repeated `Prepare()` of the same `const` SQL is a cheap cache hit. |
| SQL Server (Microsoft.Data.SqlClient) | ❌ | `sp_prepare` handles are scoped to the open connection and lost on dispose; the server already caches plans by parameterized text, so per-command `Prepare()` is pure overhead. Rely on the plan cache. |
| SQLite (Microsoft.Data.Sqlite) | ❌ | In-process compile tied to the connection; Inquiry opens a connection per operation, which negates reuse. |
| MySQL (MySqlConnector) | ❌ | Same per-operation-connection rationale; revisit if connection retention is added. |
| Oracle (Oracle.ManagedDataAccess.Core) | ❌ | Connection-scoped; benefit depends on pooling. |

When the provider doesn't support persistent prepared statements, `PreparedStatementMode.Auto` is a silent no-op — no overhead, no harm.

### Npgsql: prefer the connection-string path

For PostgreSQL the **recommended production setup** is Npgsql's automatic preparation via the connection string, which transparently prepares statements once they cross a usage threshold:

```
Host=...;Database=...;Max Auto Prepare=20;Auto Prepare Min Usages=2
```

Inquiry's per-command `Prepare()` (`Auto`) is the cross-provider fallback; `Max Auto Prepare` is the lower-overhead option when you control the connection string.

## Parameter `DbType` metadata

Independently of the mode, the generator emits an explicit `DbType` on each generated parameter (mapped from the compile-time type). This gives `Prepare()` fixed parameter types so it doesn't re-infer (and invalidate) on every call. Types with no portable `DbType` (e.g. `byte[]`) emit no assignment and fall back to provider inference.

> **Note — `System.DateTime` maps to `DbType.DateTime2`, not `DbType.DateTime`.** The explicit `DbType` is emitted even when preparation is off. SqlClient maps `DbType.DateTime` to the legacy `datetime` SQL type (range 1753+, ~3.33 ms precision), which can truncate or throw against modern `datetime2` columns; `DbType.DateTime2` round-trips both and is the modern default. (Oracle overrides this to `DbType.DateTime` for ODP.NET; Npgsql / SQLite / MySQL treat the two equivalently.)

## Caveats

- **`Compare.In` predicates are not prepared-reuse-friendly.** Variadic `IN` expansion rewrites the command text per cardinality, so the text is no longer constant and a prepared statement is not reused across different list lengths. A future option is array parameters (`= ANY(@ids)` on PostgreSQL), which keep the SQL constant.
- **Connection lifecycle is the crux.** Inquiry opens and disposes a connection per operation, so only Npgsql's pool-level prepared-statement cache and the *transacted* pipeline (which holds one connection across the transaction) see real reuse.

## Stored procedures are skipped

The pipeline never calls `PrepareAsync` for a stored procedure (`CommandType.StoredProcedure`) — they're already in the procedure cache on the server.
