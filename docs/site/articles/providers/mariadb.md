# MariaDB

MariaDB schema DDL supports composite and unique `[InquiryIndex]`, `[InquiryCheck]`, and named foreign-key
actions except `SetDefault`. Covering `Include` columns are rejected instead of being appended to the key.

Package: `Inquiry.MariaDb`. Built on `MySqlConnector` (wire-compatible with MariaDB). Targets MariaDB 10.6+.

> MariaDB users previously running on `Inquiry.MySql` should migrate to this package: swap the
> package reference, change `[assembly: InquiryDialect("MySql")]` to `"MariaDb"`, and replace
> `AddInquiryMySql(...)` with `AddInquiryMariaDb(...)`. The MariaDB builder uses native
> `INSERT…RETURNING` (halving round trips for insert-returning and upsert-returning operations)
> and does not require `AllowUserVariables` on the connection string.

## Install

```bash
dotnet add package Inquiry.MariaDb
```

```csharp
[assembly: Inquiry.InquiryDialect("MariaDb")]
```

```csharp
services.AddInquiryMariaDb("Server=localhost;Database=app;User=app;Password=…");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `` `Backticked` `` |
| Parameter prefix | `@name` |
| Auto-key | `AUTO_INCREMENT` |
| Upsert | `INSERT … ON DUPLICATE KEY UPDATE …` |
| Insert-returning | Native `INSERT … RETURNING` (MariaDB 10.5+) |
| Upsert-returning | Native `INSERT … ON DUPLICATE KEY UPDATE … RETURNING` |
| Delete-returning | Native `DELETE … RETURNING` via `[InquiryDelete(ReturnEntity = true)]` |
| Update-returning | Emulated two-statement batch (`UPDATE …; SELECT …`) — MariaDB lacks `UPDATE…RETURNING` |
| IN binding | `JSON_TABLE` subquery (MariaDB 10.6+): `col IN (SELECT jt.val FROM JSON_TABLE(@param, …) jt)` — constant SQL, single parameter |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `TINYINT(1)` (0/1) |
| String | `VARCHAR(N)` / `LONGTEXT` |
| JSON (`[InquiryJson]`) | Stored as text (`VARCHAR(N)` / `LONGTEXT`); native `JSON` only via `[InquiryColumn(SqlType = "JSON")]` |
| Soft-delete literal | `` `IsDeleted` = 0 `` |
| Full-text-search | `MATCH(...) AGAINST (@query IN NATURAL LANGUAGE MODE)` |

## Notes

- **Native `RETURNING`:** the MariaDB builder uses MariaDB 10.5+ native `INSERT…RETURNING` and
  `INSERT…ON DUPLICATE KEY UPDATE…RETURNING` for insert-returning and upsert-returning operations.
  This halves round trips compared to the emulated two-statement batch that MySQL requires.
  `UPDATE…RETURNING` is not supported by MariaDB, so update-returning stays emulated.
- **Delete returning:** declare a by-key method returning `Task<TEntity?>` and set
  `ReturnEntity = true` to receive the deleted row, or `null` when no row matches. On an entity with
  `[InquirySoftDelete]`, use `HardDelete = true`; MariaDB has no `UPDATE…RETURNING`, so soft-delete
  returning is rejected at compile time with `INQ039` rather than being changed into a physical delete.
- **No `AllowUserVariables` required:** unlike the MySQL provider, MariaDB's native `RETURNING`
  eliminates the collision-safe capture variable that MySQL's emulated path needs for non-auto
  database-default keys, so `AllowUserVariables` is not forced on the connection string.
- **Connection pooling:** the factory builds an app-lifetime `MySqlDataSource` (MySqlConnector's
  recommended pooled primitive) and opens connections from it — the foundation for future Aspire
  integration. The data source is disposed when the DI container shuts down.
- **Cloud transient-fault retry:** set `Compatibility = MariaDbCompatibility.CloudHosted` in the
  options overload to enable exponential-backoff retry on transient connection-open errors (too many
  connections, server gone away, connection reset). Tunable via `MaxAttempts`, `RetryBaseDelay`, and
  `RetryMaxDelay`. Disabled by default (`MariaDbCompatibility.None`).
- **Prepared statements:** server-side, per-connection. Inquiry's default `PreparedStatementMode.Auto` is currently a no-op for MariaDB because the provider does not advertise persistent prepared-state reuse across the per-operation connection lifecycle.
- **`max_allowed_packet`:** bulk inserts and updates respect server-side packet limits — chunk your batches if you exceed the default 64 MB.
- **Case sensitivity:** identifier case-sensitivity depends on the server's `lower_case_table_names` setting and OS. Inquiry always emits backticked identifiers matching your C# property casing.

## Testing

`tests/Inquiry.MariaDb.Tests` runs against a Testcontainers-managed `mariadb:11.4` image.
