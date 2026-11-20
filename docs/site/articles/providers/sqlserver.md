# SQL Server

SQL Server schema DDL supports composite and unique `[InquiryIndex]` declarations, covering `Include`
columns, `[InquiryCheck]`, and named foreign keys with `Cascade`, `SetNull`, and `SetDefault` actions.
`Restrict` is rejected rather than rewritten to `NoAction`.

Package: `Ignyte.Inquiry.SqlServer`. Built on `Microsoft.Data.SqlClient`.

## Install

```bash
dotnet add package Ignyte.Inquiry.SqlServer --prerelease
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
| IN binding | `col IN (SELECT [Value] FROM @param)` (table-valued parameters), or `col IN (SELECT [value] FROM OPENJSON(@param) WITH ([value] <column type> '$'))` when the criterion opts into [JSON binding](#in-lists-without-table-types); integer lists of up to 16 values bind as padded scalar parameters either way (see [Prepared statements](../features/prepared-statements.md#caveats)) |
| Full-text search | `WHERE FREETEXT(([col1], [col2]), @query)` (requires a full-text catalog + index) |
| JSON (`[InquiryJson]`) | Stored as `NVARCHAR(MAX)` (serialized text); native JSON only via explicit `[InquiryColumn(SqlType = "...")]` override. JSON-path querying renders `JSON_VALUE([col], '$.path')` |
| Update-returning | `OUTPUT … INTO @_out; SELECT … FROM @_out` (trigger-safe) |
| Upsert-returning | `OUTPUT … INTO @_out; SELECT … FROM @_out` (trigger-safe) |

## Notes

- **Upsert and IDENTITY columns:** the generator uses an `UPDATE … IF @@ROWCOUNT = 0 INSERT` pattern with `UPDLOCK, SERIALIZABLE` table hints. The IDENTITY column is excluded from the `INSERT` clause — SQL Server rejects an explicit value for an IDENTITY column.
- **Azure SQL retry policy (opt-in):** off by default (`Compatibility = None`). Configure it with `AddInquirySqlServer(cs, o => o.Compatibility = SqlServerCompatibility.AzureSql)`, and the connection factory then retries connection opens on known transient codes (40613, 40197, etc.) with exponential backoff. The default registration applies no open-time retry.
- **Encryption is mandatory by default** (Microsoft.Data.SqlClient defaults `Encrypt=Mandatory`; Inquiry ships SqlClient 7.0.1 and passes your connection string through unchanged). For LocalDB, a self-signed cert, or a non-TLS dev server, add `Encrypt=False` or `TrustServerCertificate=True` to your connection string, or supply a trusted certificate.
- **DateTime kind:** SqlClient reads `datetime2`, `datetime`, `smalldatetime` and `date` as `DateTime` with `DateTimeKind.Unspecified`. Declare `[assembly: InquiryDateTimeKind(DateTimeKind.Utc)]` when the application stores UTC. `datetimeoffset` columns map to `DateTimeOffset` and are not affected. See [DateTime kind](../entity-mapping.md#datetime-kind).
- **Application locks:** `AcquireAppLockAsync` takes a transaction-owned `sp_getapplock` lock and maps each return code to an outcome. See [Transactions](../features/transactions.md#application-locks-sql-server).
- **Prepared statements:** SQL Server's plan cache is automatic; the default `PreparedStatementMode.Auto` is a silent no-op.
- **TVP artifacts are migration-owned:** generated `Compare.In` methods bind deterministic, schema-qualified table-valued parameter types. Binding performs no catalog query, cache lookup, DDL, or connection open. Apply `InquiryGeneratedSchema.ProviderArtifactsDdl` during deployment. `ProviderArtifactsValidationSql` reports `missing`, `mismatched`, or `metadata-invisible` types without changing the database. The [migrations recipe](../features/migrations.md) shows the deployment order, permissions and how to retire old types. A list of up to 16 integers binds scalar parameters and does not use the type, so a missing type or missing `EXECUTE` permission fails only a call with more values; run the validation query during deployment.
- **The physical signature is exact:** the v2 artifact identity includes the provider storage type and facets (`VARCHAR(37)`, `DECIMAL(29,7)`, `VARBINARY(17)`, `DATETIMEOFFSET(3)`, and so on) plus `NULL`/`NOT NULL`. Converters and enums use their effective provider type. ANSI/Unicode, fixed/variable length, precision, scale, binary length, temporal scale, and nullable collection rows are not collapsed into a coarse type.
- **Supported primitive TVP storage:** signed integer partners, `BIT`, `REAL`, `FLOAT`, `DECIMAL`, `CHAR`/`VARCHAR`/`NCHAR`/`NVARCHAR`, `BINARY`/`VARBINARY`, `UNIQUEIDENTIFIER`, `DATE`, `DATETIME`, `SMALLDATETIME`, `DATETIME2`, `DATETIMEOFFSET`, and `TIME`. An explicit `SqlType` must use this primitive grammar and be compatible with the effective provider CLR type. Explicit `SqlType` cannot be combined with Inquiry length/Unicode/precision/scale facets; an unsafe or ambiguous mapping is build error `INQ076`, and no usable generated method or artifact is emitted.
- **Schemas qualify artifact identity:** the same deterministic type name can exist independently as `dbo.Inquiry_Tvp_...` and `tenant.Inquiry_Tvp_...`. The additive setup DDL creates a missing custom schema before its types.
- **Unsigned native collections:** `sbyte`, `ushort`, `uint`, and `ulong` collection elements are losslessly reinterpreted as the same-width provider-supported partners `byte`, `short`, `int`, and `long`. TVPs reuse the existing signed artifact signatures; values above the signed maximum and unsigned-backed enums retain their exact bit patterns. Nullable elements remain nullable and do not invoke converters when null.
- **Binding is lazy and single-pass:** a null or empty source binds a zero-row TVP with the exact `TypeName`. A nonempty source is peeked once and streamed without an intermediate list or table. Nullable elements become `DBNull` rows; an unexpected null for a `NOT NULL` artifact reports its element index. Inquiry-owned pipelines dispose the retained enumerator on success, failure, cancellation, early stream/grid disposal, and abandoned commands.
- **TVPs ignore `MaxBatchSize`:** a TVP-bound collection (`Compare.In` or a stored-procedure `TvpTypeName` parameter) accepts any `IEnumerable<T>` and is sent whole, as one parameter in one command. `InquiryOptions.MaxBatchSize` sizes entity-collection insert and update chunks only. It is not an input-size limit, so check the collection size before the call if your application needs one. See [Batch operations](../features/batch-operations.md#maxbatchsize-is-not-an-input-limit).
- **Custom pipeline/direct binder lifetime:** generated stores need no special handling. A custom pipeline or direct caller of the generated-support `InquiryTvpParameter.Bind` API must invoke `Inquiry.Commands.InquiryCommandResources.Dispose(dbCommand)` before disposing the command/connection. If execution and cleanup both fail, retain the execution failure as the first `AggregateException.InnerExceptions` entry and append cleanup failures in disposal order; do not let a `finally` cleanup replace the operation failure.
- **Cancellation boundary:** enumeration failures before execution do not send the statement. Once SqlClient sends a request, cancellation, timeout, transport failure, or a broken connection can leave autocommit outcome unknown; use an explicit transaction and confirm rollback before claiming atomicity. Synchronous `IEnumerable<T>.MoveNext()` cannot be interrupted by a cancellation token, and cleanup occurs after SqlClient stops consuming it.

## IN lists without table types

By default, a `Compare.In` list of more than 16 integers, or of any other type, binds as a table-valued
parameter. That needs the generated `Inquiry_Tvp_*` type in the database, with `EXECUTE` and `REFERENCES`
granted to the application. If your database must not hold Inquiry-owned objects, bind the list as JSON
instead:

```csharp
// Every Compare.In criterion in this assembly.
[assembly: Inquiry.SqlServer.SqlServerInListBinding(InListBinding.Json)]

// Or one criterion. InListBinding.TableValuedParameter keeps the table type when the assembly default is JSON.
[InquirySelectAllByPredicate, InquiryWhere("Code", Compare.In, InListBinding = InListBinding.Json)]
public partial Task<IReadOnlyList<Product>> ByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default);
```

The generated SQL is `[Code] IN (SELECT [value] FROM OPENJSON(@Code) WITH ([value] NVARCHAR(40) '$'))`, and
the list is sent as one `nvarchar(max)` JSON array.

- **No artifact:** a criterion that uses JSON adds no type to `ProviderArtifactsDdl` or
  `ProviderArtifactsValidationSql`. When no criterion in the assembly uses a table type, the generated
  schema has neither constant.
- **Typed as the column:** the `WITH` clause gives each element the column's own SQL type, from its
  facets or its explicit `SqlType`, such as `UNIQUEIDENTIFIER`, `DATETIME2(3)` or `DECIMAL(21,5)`. The
  comparison then uses the column's type and collation, not `nvarchar`, as it does with a table type.
  Temporal values are truncated to the column scale and `datetime` values are rounded to its 1/300 s grid,
  as SqlClient does for a table-valued parameter. `byte[]` values are sent as base64, which `OPENJSON` decodes.
- **Same rules otherwise:** lists of up to 16 integers still bind as scalar parameters, `Compare.NotIn`
  still expands to scalar parameters, and null or empty lists match no rows.
- **Compatibility level 130 or later:** `OPENJSON` needs SQL Server 2016 or later with database
  compatibility level 130 or higher. Azure SQL Database and Azure SQL Managed Instance support it.
- **Query plans:** SQL Server uses a fixed row estimate for `OPENJSON`, whatever the list length. For a
  table-valued parameter it uses the row count of the call that compiled the plan. Both shapes keep one
  cached plan for every list length. For very large
  lists that join large tables, compare the plans and keep the table type on the criteria where it is
  faster. JSON also costs some CPU to serialize and parse the list, which the table type streams instead.
  Like a table type, the JSON parameter is not limited by `MaxBatchSize` or the 2,100-parameter limit.

## Testing

`tests/Inquiry.SqlServer.Tests` runs against a Testcontainers-managed `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` image (a pinned 2022 build, not the rolling `:latest` tag). Skips gracefully when Docker is absent.
