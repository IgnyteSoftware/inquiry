# MySQL

Package: `Inquiry.MySql`. Built on `MySqlConnector`.

> **MariaDB users:** use the dedicated [`Inquiry.MariaDb` package](mariadb.md) instead. The two
> dialects were split in #168 — today they emit identical SQL, but MariaDB-specific features
> (native `RETURNING`, `JSON_TABLE` IN binding) will only land in the MariaDB provider.

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
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `TINYINT(1)` (0/1) |
| String | `VARCHAR(N)` / `LONGTEXT` |
| JSON (`[InquiryJson]`) | Stored as text (`VARCHAR(N)` / `LONGTEXT`); native `JSON` only via `[InquiryColumn(SqlType = "JSON")]` |
| Soft-delete literal | `` `IsDeleted` = 0 `` |
| Full-text-search | `MATCH(...) AGAINST (@query IN NATURAL LANGUAGE MODE)` |

## Notes

- **`AllowUserVariables` and ad-hoc SQL:** Inquiry enables `AllowUserVariables=true` on MySQL connections (required for generated-key upserts that use `@_inquiry_genkey`). A side effect is that a **misspelled `@param`** in hand-written ad-hoc SQL (the `IInquiry.Query*`/`Execute*` `FormattableString` overloads or an `InquiryCommand`) is silently treated as a **NULL MySQL user variable** instead of throwing a "parameter not found" error. Generated store methods are unaffected — their SQL and parameter names are compile-time constants. If you write ad-hoc SQL against MySQL, double-check your parameter names; a typo will produce `NULL` values with no error. See [Security](../security.md#mysql-user-variables-caveat).
- **Prepared statements:** server-side, per-connection. Inquiry's default `PreparedStatementMode.Auto` is currently a no-op for MySQL because the provider does not advertise persistent prepared-state reuse across the per-operation connection lifecycle.
- **`max_allowed_packet`:** bulk inserts and updates respect server-side packet limits — chunk your batches if you exceed the default 64 MB.
- **Case sensitivity:** identifier case-sensitivity depends on the server's `lower_case_table_names` setting and OS. Inquiry always emits backticked identifiers matching your C# property casing.

## Testing

`tests/Inquiry.MySql.Tests` runs against a Testcontainers-managed `mysql:8` image.
