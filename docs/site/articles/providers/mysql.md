# MySQL

Package: `Inquiry.MySql`. Built on `MySqlConnector`.

> **MariaDB users:** use the dedicated [`Inquiry.MariaDb` package](mariadb.md) instead. The MariaDB
> provider uses native `INSERT…RETURNING` (halving round trips) and does not require
> `AllowUserVariables` on the connection string.

## Install

```bash
dotnet add package Inquiry.MySql
```

```csharp
[assembly: Inquiry.InquiryDialect("MySql")]
```

```csharp
services.AddInquiryMySql("Server=localhost;Database=app;User=app;Password=…");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `` `Backticked` `` |
| Parameter prefix | `@name` |
| Auto-key | `AUTO_INCREMENT` |
| Upsert | `INSERT … ON DUPLICATE KEY UPDATE …` |
| Insert-returning | Emulated two-statement batch (`INSERT …; SELECT …`) — keyed on `LAST_INSERT_ID()` for `AUTO_INCREMENT`, on the key predicate for client-supplied keys (no `RETURNING`) |
| IN binding | `JSON_TABLE` subquery (MySQL 8.0+): `col IN (SELECT jt.val FROM JSON_TABLE(@param, …) jt)` — constant SQL, single parameter |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `TINYINT(1)` (0/1) |
| String | `VARCHAR(N)` / `LONGTEXT` |
| JSON (`[InquiryJson]`) | Stored as text (`VARCHAR(N)` / `LONGTEXT`); native `JSON` only via `[InquiryColumn(SqlType = "JSON")]` |
| Soft-delete literal | `` `IsDeleted` = 0 `` |
| Full-text-search | `MATCH(...) AGAINST (@query IN NATURAL LANGUAGE MODE)` |

## Notes

- **`AllowUserVariables` and ad-hoc SQL:** Inquiry enables `AllowUserVariables=true` on MySQL connections (required for generated-key upserts that use `@_inquiry_genkey`). A side effect is that a **misspelled `@param`** in hand-written ad-hoc SQL (the `IInquiry.Query*`/`Execute*` `FormattableString` overloads or an `InquiryCommand`) is silently treated as a **NULL MySQL user variable** instead of throwing a "parameter not found" error. Generated store methods are unaffected — their SQL and parameter names are compile-time constants. If you write ad-hoc SQL against MySQL, double-check your parameter names; a typo will produce `NULL` values with no error. See [Security](../security.md#mysql-user-variables-caveat).
- **Connection pooling:** the factory builds an app-lifetime `MySqlDataSource` (MySqlConnector's
  recommended pooled primitive) and opens connections from it — the foundation for future Aspire
  integration. The data source is disposed when the DI container shuts down.
- **Cloud transient-fault retry:** set `Compatibility = MySqlCompatibility.CloudHosted` in the
  options overload to enable exponential-backoff retry on transient connection-open errors (too many
  connections, server gone away, connection reset). Tunable via `MaxAttempts`, `RetryBaseDelay`, and
  `RetryMaxDelay`. Disabled by default (`MySqlCompatibility.None`).
- **Prepared statements:** server-side, per-connection. Inquiry's default `PreparedStatementMode.Auto` is currently a no-op for MySQL because the provider does not advertise persistent prepared-state reuse across the per-operation connection lifecycle.
- **`max_allowed_packet`:** bulk inserts and updates respect server-side packet limits — chunk your batches if you exceed the default 64 MB.
- **Case sensitivity:** identifier case-sensitivity depends on the server's `lower_case_table_names` setting and OS. Inquiry always emits backticked identifiers matching your C# property casing.

## Testing

`tests/Inquiry.MySql.Tests` runs against a Testcontainers-managed `mysql:8` image.
