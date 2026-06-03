# Prepared statements

Opt-in via `InquiryOptions.PrepareStatements = PreparedStatementMode.Auto`. When enabled and the provider supports persistent prepared-statement caches (Npgsql, MySQL), the pipeline calls `PrepareAsync` once per command. The database keeps the parsed plan for the lifetime of the connection / pool.

## Wiring

```csharp
services.AddInquiryPostgreSql(connectionString, options =>
{
    options.PrepareStatements = PreparedStatementMode.Auto;
});
```

## Provider support

| Dialect | Persistent prepared-statement cache? |
|---|---|
| PostgreSQL (Npgsql) | ✅ Auto-prepared after configurable execution-count threshold |
| MySQL (MySqlConnector) | ✅ Server-side, per-connection |
| SQL Server | ❌ Plan cache is automatic; explicit `Prepare` is a no-op |
| Sqlite | ❌ Per-statement; no cross-statement reuse |
| Oracle | ⚠️ Connection-scoped; benefit depends on pooling |

When the provider doesn't support persistent prepared statements, `PreparedStatementMode.Auto` is a silent no-op — no overhead, no harm.

## Stored procedures are skipped

The pipeline never calls `PrepareAsync` for a stored procedure (`CommandType.StoredProcedure`) — they're already in the procedure cache on the server.

## See also

The existing repository doc [`docs/prepared-statements.md`](https://github.com/JakeOverstreet/inquiry/blob/main/docs/prepared-statements.md) has the full design rationale.
