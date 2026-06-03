# PostgreSQL

Package: `Inquiry.PostgreSql`. Built on `Npgsql`.

## Install

```bash
dotnet add package Inquiry.PostgreSql
```

```csharp
[assembly: Inquiry.InquiryDialect("PostgreSql")]
```

```csharp
services.AddInquiryPostgreSql("Host=localhost;Database=app;Username=app;Password=…");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `"Quoted"` (case-sensitive) |
| Parameter prefix | `@name` (Npgsql accepts both `@` and `:`) |
| Auto-key | `GENERATED ALWAYS AS IDENTITY` (or `SERIAL` legacy) |
| Upsert | `INSERT … ON CONFLICT (pk) DO UPDATE SET …` |
| Insert-returning | `INSERT … RETURNING *` |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `BOOLEAN` (true/false) |
| String | `TEXT` |
| JSON | `JSONB` |
| Soft-delete literal | `"IsDeleted" = FALSE` |
| Full-text-search | `to_tsvector(...) @@ plainto_tsquery(@query)` |

## Notes

- **Prepared statements:** Npgsql has a per-connection auto-prepare cache. Enable with `PreparedStatementMode.Auto`. The first N executions of a statement go through normal `Parse/Bind/Execute`; after the threshold, Npgsql auto-prepares it and reuses the parsed plan for the connection's lifetime.
- **Identifier case:** unlike SQL Server, PostgreSQL folds unquoted identifiers to lowercase. Inquiry always emits quoted identifiers to preserve the C# property casing, so `[InquiryColumn] public string CompanyName` stays as `"CompanyName"` in the database.
- **Cloud retry:** Aurora-style transient errors (`08001`, `08006`, certain `40xxx` codes) are auto-retried.
- **Stored functions vs procedures:** Npgsql 5+ handles both kinds under `CommandType.StoredProcedure`. Functions are wrapped as `SELECT * FROM fn(args)`; procedures use `CALL proc(args)`.

## Testing

`tests/Inquiry.PostgreSql.Tests` runs against a Testcontainers-managed `postgres:16` image.
