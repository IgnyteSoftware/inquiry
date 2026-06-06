# Prepared statements

Inquiry bakes every statement as a stable `const string`, which makes it a natural fit for server-side prepared statements. Preparation is **on by default** and **capability-gated**.

## Wiring

Most apps do not need to configure it:

```csharp
services
    .AddInquiry()
    .AddInquiryPostgreSql(connectionString);
```

To opt out:

```csharp
services
    .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.None)
    .AddInquiryPostgreSql(connectionString);
```

- `PreparedStatementMode.Auto` (**default**) - the pipeline calls `PrepareAsync` once before execution, but **only** on providers whose connections retain prepared state across the pooled-connection lifecycle, and never for `CommandType.StoredProcedure`.
- `PreparedStatementMode.None` - opt out; the pipeline never calls `Prepare()`.

## Provider support

`IInquiryConnectionFactory.SupportsPersistentPreparedStatements` gates whether `Auto` actually calls `PrepareAsync`. **Only PostgreSQL (Npgsql) returns `true` today**; the rest inherit the `false` default.

| Dialect | Persistent prepared-statement cache? | Why |
|---|:--:|---|
| PostgreSQL (Npgsql) | ✅ | Prepared statements are cached on the **pooled physical connection** and survive the logical connection being disposed, so repeated `Prepare()` of the same `const` SQL is a cheap cache hit. |
| SQL Server (Microsoft.Data.SqlClient) | ❌ | `sp_prepare` handles are scoped to the open connection and lost on dispose; the server already caches plans by parameterized text, so per-command `Prepare()` is pure overhead. Rely on the plan cache. |
| SQLite (Microsoft.Data.Sqlite) | ❌ | In-process compile tied to the connection; Inquiry opens a connection per operation, which negates reuse. |
| MySQL (MySqlConnector) | ❌ | Same per-operation-connection rationale; revisit if connection retention is added. |
| Oracle (Oracle.ManagedDataAccess.Core) | ❌ | Connection-scoped; benefit depends on pooling. |

When the provider doesn't support persistent prepared statements, the default `PreparedStatementMode.Auto` is a silent no-op - no overhead, no harm.

### PostgreSQL: two preparation policies

On PostgreSQL, Inquiry's default `Auto` mode uses explicit `PrepareAsync` because Npgsql persists prepared state on pooled physical connections. If you prefer Npgsql's usage-threshold policy, opt out of Inquiry preparation and enable automatic preparation in the connection string:

```
Host=...;Database=...;Max Auto Prepare=20;Auto Prepare Min Usages=2
```

Benchmark both policies against your workload before making product claims; the better fit depends on statement reuse and connection-pool behavior.

## Benchmarking

The PostgreSQL benchmark project includes `PreparedStatementBenchmarks`, which compares
explicit `PreparedStatementMode.None` and the default `Auto` mode through the Inquiry pipeline:

- `SimplePointRead` uses a generated `ShipperStore.SelectByKeyAsync(...)` call.
- `MultiJoinPointRead` uses a stable ad-hoc parameterized SQL command that joins `Products`,
  `Categories`, and `Suppliers`, then materializes a `Product`.

Run it with Docker available:

```powershell
# Full measurement run.
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks.PostgreSql\Inquiry.Benchmarks.PostgreSql.csproj -- --filter "*PreparedStatementBenchmarks*" --inProcess

# Fast wiring smoke run.
dotnet run -c Release --project benchmarks\Inquiry.Benchmarks.PostgreSql\Inquiry.Benchmarks.PostgreSql.csproj -- --filter "*PreparedStatementBenchmarks*" --job short --inProcess
```

Use `--job short` only to smoke-test wiring.

Latest full run, on 2026-06-06, used BenchmarkDotNet 0.14.0, .NET 8.0.24, Docker PostgreSQL
(`postgres:16-alpine`), and `--inProcess` so the shared Testcontainer is reused:

| Method | Mean | Ratio | Allocated | Alloc ratio |
|---|---:|---:|---:|---:|
| `MultiJoinPointRead_None` | 944.5 us | 1.02 | 4.39 KB | 1.00 |
| `MultiJoinPointRead_Auto` | 713.5 us | 0.77 | 4.02 KB | 0.92 |
| `SimplePointRead_None` | 662.6 us | 1.02 | 2.98 KB | 1.00 |
| `SimplePointRead_Auto` | 587.9 us | 0.90 | 2.93 KB | 0.98 |

BenchmarkDotNet flagged multimodal or bimodal distributions in that run, which is common for
networked container benchmarks on a workstation. Treat the table as evidence that Npgsql preparation is
worth keeping available, not as a universal ratio guarantee; rerun on the target workload and hardware
before making product claims.

## Parameter `DbType` metadata

Independently of the mode, the generator emits an explicit `DbType` on each generated parameter (mapped from the compile-time type). This gives `Prepare()` fixed parameter types so it doesn't re-infer (and invalidate) on every call. Types with no portable `DbType` (e.g. `byte[]`) emit no assignment and fall back to provider inference.

> **Note — `System.DateTime` maps to `DbType.DateTime2`, not `DbType.DateTime`.** The explicit `DbType` is emitted even when preparation is off. SqlClient maps `DbType.DateTime` to the legacy `datetime` SQL type (range 1753+, ~3.33 ms precision), which can truncate or throw against modern `datetime2` columns; `DbType.DateTime2` round-trips both and is the modern default. (Oracle overrides this to `DbType.DateTime` for ODP.NET; Npgsql / SQLite / MySQL treat the two equivalently.)

## Caveats

- **`Compare.In` predicates are not prepared-reuse-friendly.** Variadic `IN` expansion rewrites the command text per cardinality, so the text is no longer constant and a prepared statement is not reused across different list lengths. A future option is array parameters (`= ANY(@ids)` on PostgreSQL), which keep the SQL constant.
- **Connection lifecycle is the crux.** Inquiry opens and disposes a connection per operation, so only Npgsql's pool-level prepared-statement cache and the *transacted* pipeline (which holds one connection across the transaction) see real reuse.

## Stored procedures are skipped

The pipeline never calls `PrepareAsync` for a stored procedure (`CommandType.StoredProcedure`) — they're already in the procedure cache on the server.
