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

- **Prepared statements:** `PreparedStatementMode.Auto` is the Inquiry default, and PostgreSQL is the provider that currently opts into it. Npgsql keeps prepared statements on the pooled physical connection, so stable Inquiry SQL can reuse the parsed plan across logical connection opens. If you prefer Npgsql's usage-threshold auto-prepare policy, set Inquiry to `PreparedStatementMode.None` and configure `Max Auto Prepare` / `Auto Prepare Min Usages` in the connection string.
- **Array `IN` parameters:** `Compare.In` predicates, `[InquiryDeleteAll]`, and IN criteria on set-based mutations render `col = ANY(@ids)` and bind the collection as one native array parameter. The SQL stays constant across list lengths (so prepared statements keep being reused — see [Prepared statements](../features/prepared-statements.md)), and the per-element parameter cap does not apply to IN lists. An empty collection binds an empty array and matches no rows; enum elements are coerced to their underlying integral type.
- **Identifier case:** unlike SQL Server, PostgreSQL folds unquoted identifiers to lowercase. Inquiry always emits quoted identifiers to preserve the C# property casing, so `[InquiryColumn] public string CompanyName` stays as `"CompanyName"` in the database.
- **Cloud retry:** Aurora-style transient errors (`08001`, `08006`, certain `40xxx` codes) are auto-retried.
- **Stored functions vs procedures:** Npgsql 5+ handles both kinds under `CommandType.StoredProcedure`. Functions are wrapped as `SELECT * FROM fn(args)`; procedures use `CALL proc(args)`.

## Testing

`tests/Inquiry.PostgreSql.Tests` runs against a Testcontainers-managed `postgres:16` image.
