# MariaDB

Package: `Inquiry.MariaDb`. Built on `MySqlConnector` (wire-compatible with MariaDB). Targets MariaDB 10.6+.

> MariaDB users previously running on `Inquiry.MySql` should migrate to this package: swap the
> package reference, change `[assembly: InquiryDialect("MySql")]` to `"MariaDb"`, and replace
> `AddInquiryMySql(...)` with `AddInquiryMariaDb(...)`. The generated SQL is currently identical
> (the dialect split, #168, is behavioral-parity re-plumbing), but MariaDB-specific improvements —
> native `INSERT…RETURNING` (#58), the `JSON_TABLE` IN optimization (#170) — land only here.

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
| Insert-returning | Emulated two-statement batch (`INSERT …; SELECT …`) — keyed on `LAST_INSERT_ID()` for `AUTO_INCREMENT`, on the key predicate for client-supplied keys (native `RETURNING` is tracked as #58) |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `TINYINT(1)` (0/1) |
| String | `VARCHAR(N)` / `LONGTEXT` |
| JSON (`[InquiryJson]`) | Stored as text (`VARCHAR(N)` / `LONGTEXT`); native `JSON` only via `[InquiryColumn(SqlType = "JSON")]` |
| Soft-delete literal | `` `IsDeleted` = 0 `` |
| Full-text-search | `MATCH(...) AGAINST (@query IN NATURAL LANGUAGE MODE)` |

## Notes

- **Behavioral parity with MySQL:** the MariaDB builder currently emits SQL identical to the MySQL
  builder (both derive from the shared MySQL-family builder). MariaDB-specific extensions (e.g.
  `RETURNING`) will diverge in follow-up work (#58, #170).
- **Prepared statements:** server-side, per-connection. Inquiry's default `PreparedStatementMode.Auto` is currently a no-op for MariaDB because the provider does not advertise persistent prepared-state reuse across the per-operation connection lifecycle.
- **`max_allowed_packet`:** bulk inserts and updates respect server-side packet limits — chunk your batches if you exceed the default 64 MB.
- **Case sensitivity:** identifier case-sensitivity depends on the server's `lower_case_table_names` setting and OS. Inquiry always emits backticked identifiers matching your C# property casing.

## Testing

`tests/Inquiry.MariaDb.Tests` runs against a Testcontainers-managed `mariadb:11.4` image.
