# Oracle

Package: `Inquiry.Oracle`. Built on `Oracle.ManagedDataAccess.Core`.

## Install

```bash
dotnet add package Inquiry.Oracle
```

```csharp
[assembly: Inquiry.InquiryDialect("Oracle")]
```

```csharp
services.AddInquiryOracle("User Id=app;Password=…;Data Source=//localhost:1521/XEPDB1");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `"UPPER_CASED"` (Oracle folds unquoted to upper; Inquiry quotes everything and folds the C# property name to upper) |
| Parameter prefix | `:name` (rewritten from `@name` in the connection factory's `FinalizeCommand`) |
| Bind mode | `BindByName = true` set on every command |
| Auto-key | `GENERATED ALWAYS AS IDENTITY` (12c+) |
| Upsert | `MERGE INTO … USING (SELECT … FROM dual)` |
| Insert-returning | `RETURNING … INTO :out_<col>` (OUT params bound by the connection factory) |
| Pagination | `OFFSET :offset ROWS FETCH NEXT :limit ROWS ONLY` (12c+) |
| Boolean | `NUMBER(1)` (0/1) — Oracle has no native BOOLEAN until 23c |
| String | `VARCHAR2(N)` / `NVARCHAR2(N)` / `CLOB` |
| JSON | `JSON` (21c+) or `CLOB IS JSON` |
| Soft-delete literal | `"ISDELETED" = 0` |
| Full-text-search | `CONTAINS("Title" \|\| ' ' \|\| "Body", :query) > 0` (requires Oracle Text index) |

## Notes

- **Connection factory does provider-specific fixups:** `BindByName = true` (so `:name` references bind by name, not position), `@`-to-`:` parameter renaming, and OUT-parameter binding for `RETURNING ... INTO` blocks.
- **`INSERT ALL` for batch inserts:** Oracle doesn't support multi-row `VALUES`. The generator emits `INSERT ALL INTO t (...) VALUES (...) INTO t (...) VALUES (...) SELECT 1 FROM dual`.
- **`UpdateAll` is unsupported** — Oracle has no clean equivalent of multi-row `UPDATE … VALUES`. The generator emits a throwing stub (`INQ039` warning).
- **`Upsert` with DB-generated key** — the upsert path requires a known key. For a DB-generated key, use `InsertAsync` for new rows and `UpdateAsync` for existing ones; `UpsertAsync` is a throwing stub in that scenario.
- **Stored-procedure result sets** (today) require an OUT `SYS_REFCURSOR` parameter, which the generator doesn't yet emit. Either use a `FUNCTION` that `RETURN SYS_REFCURSOR`, or wait for the planned stored-procedure expansion.

## Testing

`tests/Inquiry.Oracle.Tests` runs against a Testcontainers-managed `gvenzl/oracle-xe:21-slim-faststart` image, in the per-PR CI integration matrix alongside the other engines (~3 min container warm-up).
