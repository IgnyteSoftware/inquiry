# MySQL / MariaDB

Package: `Inquiry.MySql`. Built on `MySqlConnector`.

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
| Insert-returning | `LAST_INSERT_ID()` round-trip (MySQL pre-8.0.21); `RETURNING` (MariaDB 10.5+) |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `TINYINT(1)` (0/1) |
| String | `VARCHAR(N)` / `LONGTEXT` |
| JSON | `JSON` |
| Soft-delete literal | `` `IsDeleted` = 0 `` |
| Full-text-search | `MATCH(...) AGAINST (@query IN NATURAL LANGUAGE MODE)` |

## Notes

- **MariaDB compatibility:** the generator's MySQL dialect targets the MySQL feature set; MariaDB-specific extensions (e.g. `RETURNING`) are not emitted by default to keep the compiled SQL portable.
- **Prepared statements:** server-side, per-connection. Inquiry's default `PreparedStatementMode.Auto` is currently a no-op for MySQL because the provider does not advertise persistent prepared-state reuse across the per-operation connection lifecycle.
- **`max_allowed_packet`:** bulk inserts and updates respect server-side packet limits — chunk your batches if you exceed the default 64 MB.
- **Case sensitivity:** identifier case-sensitivity depends on the server's `lower_case_table_names` setting and OS. Inquiry always emits backticked identifiers matching your C# property casing.

## Testing

`tests/Inquiry.MySql.Tests` runs against a Testcontainers-managed `mysql:8` image.
