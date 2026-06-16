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
| String | `NVARCHAR(MAX)` (or sized per `[InquiryColumn(Length = N)]`) |
| Soft-delete literal | `[IsDeleted] = 0` |

## Notes

- **`MERGE` and IDENTITY columns:** the generator omits the IDENTITY column from the `INSERT` clause of the `MERGE` — SQL Server rejects an explicit value for an IDENTITY column even in a not-taken branch. (PostgreSQL and MySQL are lenient here; Oracle's MERGE syntax also omits it.)
- **Azure SQL retry policy (opt-in):** off by default (`Compatibility = None`). Configure it with `AddInquirySqlServer(cs, o => o.Compatibility = SqlServerCompatibility.AzureSql)`, and the connection factory then retries connection opens on known transient codes (40613, 40197, etc.) with exponential backoff. The default registration applies no open-time retry.
- **Encryption is mandatory by default** (Microsoft.Data.SqlClient defaults `Encrypt=Mandatory`; Inquiry ships SqlClient 7.0.1 and passes your connection string through unchanged). For LocalDB, a self-signed cert, or a non-TLS dev server, add `Encrypt=False` or `TrustServerCertificate=True` to your connection string, or supply a trusted certificate.
- **Prepared statements:** SQL Server's plan cache is automatic; the default `PreparedStatementMode.Auto` is a silent no-op.

## Testing

`tests/Inquiry.SqlServer.Tests` runs against a Testcontainers-managed `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` image (a pinned 2022 build, not the rolling `:latest` tag). Skips gracefully when Docker is absent.
