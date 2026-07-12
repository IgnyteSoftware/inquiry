# SQL Server

SQL Server schema DDL supports composite and unique `[InquiryIndex]` declarations, covering `Include`
columns, `[InquiryCheck]`, and named foreign keys with `Cascade`, `SetNull`, and `SetDefault` actions.
`Restrict` is rejected rather than rewritten to `NoAction`.

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
| Upsert | `UPDATE … IF @@ROWCOUNT = 0 INSERT` (with `UPDLOCK, SERIALIZABLE` table hints; excludes IDENTITY column from INSERT) |
| Insert-returning | `OUTPUT … INTO @_out; SELECT … FROM @_out` (trigger-safe) |
| Pagination | `OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY` |
| Boolean | `BIT` |
| String | `NVARCHAR(MAX)` (or sized per `[InquiryColumn(Length = N)]`) |
| Soft-delete literal | `[IsDeleted] = 0` |
| IN binding | `col IN (SELECT [Value] FROM @param)` (table-valued parameters) |
| Full-text search | `WHERE FREETEXT(([col1], [col2]), @query)` (requires a full-text catalog + index) |
| JSON (`[InquiryJson]`) | Stored as `NVARCHAR(MAX)` (serialized text); native JSON only via explicit `[InquiryColumn(SqlType = "...")]` override. JSON-path querying renders `JSON_VALUE([col], '$.path')` |
| Update-returning | `OUTPUT … INTO @_out; SELECT … FROM @_out` (trigger-safe) |
| Upsert-returning | `OUTPUT … INTO @_out; SELECT … FROM @_out` (trigger-safe) |

## Notes

- **Upsert and IDENTITY columns:** the generator uses an `UPDATE … IF @@ROWCOUNT = 0 INSERT` pattern with `UPDLOCK, SERIALIZABLE` table hints. The IDENTITY column is excluded from the `INSERT` clause — SQL Server rejects an explicit value for an IDENTITY column.
- **Azure SQL retry policy (opt-in):** off by default (`Compatibility = None`). Configure it with `AddInquirySqlServer(cs, o => o.Compatibility = SqlServerCompatibility.AzureSql)`, and the connection factory then retries connection opens on known transient codes (40613, 40197, etc.) with exponential backoff. The default registration applies no open-time retry.
- **Encryption is mandatory by default** (Microsoft.Data.SqlClient defaults `Encrypt=Mandatory`; Inquiry ships SqlClient 7.0.1 and passes your connection string through unchanged). For LocalDB, a self-signed cert, or a non-TLS dev server, add `Encrypt=False` or `TrustServerCertificate=True` to your connection string, or supply a trusted certificate.
- **Prepared statements:** SQL Server's plan cache is automatic; the default `PreparedStatementMode.Auto` is a silent no-op.
- **TVP artifacts are migration-owned:** generated `Compare.In` and `[InquiryDeleteAll]` methods bind deterministic, schema-qualified table-valued parameter types. Binding performs no catalog query, cache lookup, DDL, or connection open. Apply `InquiryGeneratedSchema.ProviderArtifactsDdl` during deployment. `ProviderArtifactsValidationSql` reports missing types without changing the database.
- **Schemas are part of artifact identity:** an entity in schema `tenant` gets a `tenant.Inquiry_Tvp_...` type, distinct from the same element type in `dbo`. The additive setup DDL creates a missing custom schema before its types.
- **Unsigned native collections:** `sbyte`, `ushort`, `uint`, and `ulong` collection elements are losslessly reinterpreted as the same-width provider-supported partners `byte`, `short`, `int`, and `long`. TVPs reuse the existing signed artifact signatures; values above the signed maximum and unsigned-backed enums retain their exact bit patterns. Nullable elements remain nullable and do not invoke converters when null.

## Testing

`tests/Inquiry.SqlServer.Tests` runs against a Testcontainers-managed `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` image (a pinned 2022 build, not the rolling `:latest` tag). Skips gracefully when Docker is absent.
