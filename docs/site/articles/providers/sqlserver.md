# SQL Server

Package: `Inquiry.SqlServer`. Built on `Microsoft.Data.SqlClient`.

## Install

```bash
dotnet add package Inquiry.SqlServer
```

```csharp
[assembly: Inquiry.InquiryDialect("SqlServer")]
```

```csharp
services.AddInquirySqlServer(
    "Server=(localdb)\\MSSQLLocalDB;Database=App;Trusted_Connection=true;Encrypt=false");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `[Bracketed]` |
| Parameter prefix | `@name` |
| Auto-key | `IDENTITY(1,1)` |
| Upsert | `MERGE … WHEN MATCHED … WHEN NOT MATCHED THEN INSERT (excluding IDENTITY column)` |
| Insert-returning | `OUTPUT INSERTED.*` |
| Pagination | `OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY` |
| Boolean | `BIT` |
| String | `NVARCHAR(MAX)` (or sized per `[InquiryColumn(Size = N)]`) |
| Soft-delete literal | `[IsDeleted] = 0` |

## Notes

- **`MERGE` and IDENTITY columns:** the generator omits the IDENTITY column from the `INSERT` clause of the `MERGE` — SQL Server rejects an explicit value for an IDENTITY column even in a not-taken branch. (PostgreSQL and MySQL are lenient here; Oracle's MERGE syntax also omits it.)
- **Azure SQL retry policy:** the connection factory automatically retries connection opens on known transient codes (40613, 40197, etc.) with exponential backoff.
- **Encryption defaults changed in SqlClient 4.0.** Add `Encrypt=false` for LocalDB or non-TLS dev environments, or supply a certificate.
- **Prepared statements:** SQL Server's plan cache is automatic; `PreparedStatementMode.Auto` is a silent no-op.

## Testing

`tests/Inquiry.SqlServer.Tests` runs against a Testcontainers-managed `mcr.microsoft.com/mssql/server:2022-latest` image. Skips gracefully when Docker is absent.
