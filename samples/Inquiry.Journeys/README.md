# Application journeys

This console sample walks nine end-to-end journeys against an in-memory SQLite database, using
generated stores and a handwritten report query. The journeys share one database and run in order,
and some reuse rows that earlier journeys wrote. Each journey checks its outcome, throws on any mismatch, and prints one line.
The last line is `JOURNEYS-OK`. The output is in [`expected-output.txt`](expected-output.txt), and the
package consumer check compares it exactly, on .NET 10, .NET 11 and as a NativeAOT binary.

| Journey | What it shows | Inquiry API |
|---|---|---|
| `new-schema` | Creates the database from the generated DDL and uses it | `InquiryGeneratedSchema.Ddl` |
| `existing-schema` | CRUD on a table that handwritten DDL created, with physical names that differ from the CLR names | `[InquiryTable("legacy_supplier", GenerateDdl = false)]`, `[InquiryKey("supplier_no")]`, `[InquiryColumn("company_nm")]` |
| `order-and-lines` | Saves an order and its lines in one transaction; a failed line rolls back both | `IInquiry.ExecuteInTransactionAsync`, batch `[InquiryInsert]`, `[InquiryCheck]` |
| `role-links` | Links users to roles through a junction, reads a user's roles eagerly, unlinks one | `[InquiryManyToMany]`, `[InquirySelectOneByKeyEager]`, composite-key `[InquiryDelete]` |
| `concurrent-edits` | Two readers load one row; the first update wins and the second is rejected | `[InquiryConcurrencyToken]`, `InquiryOptions.ThrowOnConcurrencyConflict`, `InquiryConcurrencyException` |
| `tenant-isolation` | Tenant 1 cannot read, update or delete tenant 2's rows; a read with no tenant throws | `[InquiryGlobalFilter(ContextKey = "TenantId", EnforceOnWrites = true)]`, `InquiryFilterContext.BeginScope` |
| `selective-eager` | An eager select loads only the relation it names | `[InquiryInclude(nameof(SalesOrder.Lines))]` |
| `handwritten-sql` | A report query with aggregates maps into a DTO by column alias; a query that lacks a column is rejected | `[InquiryAdHoc]`, `IInquiry.QueryListAsync<T>(FormattableString)` |
| `aot-json` | A JSON column round-trips through a source-generated serializer context | `[InquiryJson]`, `[assembly: InquiryJsonContext(...)]` |

Migration evolution, the tenth journey, has its own sample:
[`samples/Inquiry.SchemaEvolution`](../Inquiry.SchemaEvolution) takes a SQL Server database through
three releases with DbUp. It needs Docker, so it is not repeated here.

## Run

```bash
dotnet run --project samples/Inquiry.Journeys -c Release
```

To run it as a native binary, publish it for your runtime and run the output:

```bash
dotnet publish samples/Inquiry.Journeys -c Release -r linux-x64 -o journeys-aot
./journeys-aot/Inquiry.Journeys
```

NativeAOT needs the platform's native toolchain, such as clang on Linux or the MSVC build tools on
Windows. The journeys share one database and run in order, so each journey uses rows that the
journeys before it wrote.
