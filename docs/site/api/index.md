# API reference

Auto-generated from the XML doc comments on the public Inquiry packages.

## Namespaces

| Namespace | What lives here |
|---|---|
| `Inquiry` | The `IInquiry` facade, attributes (`InquiryTable`, `InquiryColumn`, etc.), `InquiryStore<T>` base class, options, dialect marker. |
| `Inquiry.Commands` | `InquiryCommand`, `InquiryCommandContext`, executed/failed contexts. |
| `Inquiry.Connections` | `IInquiryConnectionFactory`, `RetryingConnectionOpener`, transient-error detection. |
| `Inquiry.Converters` | Built-in value converters (`InquiryJsonConverter<T>`, etc.). |
| `Inquiry.DependencyInjection` | DI extension methods (`AddInquiryGeneratedStores`, etc.). |
| `Inquiry.Entities` | Entity-shape attributes (`InquiryTable`, `InquiryKey`, `InquiryColumn`, navigations, indexes). |
| `Inquiry.Interceptors` | `IInquiryCommandInterceptor` and the context types passed to it. |
| `Inquiry.Materialization` | `IInquiryEntityMaterializer<T>` — the generator emits implementations. |
| `Inquiry.Parameters` | `InquiryParameter`, parameter binder. |
| `Inquiry.Pipeline` | `IInquiryRequestPipeline`, the default and transacted implementations. |
| `Inquiry.Stores` | Store-method operation attributes (`InquirySelectAll`, `InquiryInsert`, `InquiryStoredProcedure`, etc.) and the `InquiryStore<T>` base. |
| `Inquiry.Sqlite` | SQLite provider: `SqliteInquiryConnectionFactory`, `AddInquirySqlite` extension. |
| `Inquiry.SqlServer` | SQL Server provider equivalents. |
| `Inquiry.PostgreSql` | PostgreSQL provider equivalents. |
| `Inquiry.MySql` | MySQL / MariaDB provider equivalents. |
| `Inquiry.Oracle` | Oracle provider equivalents. |

## Diagnostics

Inquiry emits `INQ001`–`INQ040` diagnostics at compile time. Each is documented in the source on `InquiryDiagnosticDescriptors`; they cover:

- Entity-shape errors (missing key, duplicate columns, unsupported types)
- Store-method errors (unknown column in `[InquiryWhere]`, unsupported return shape, conflicting attributes)
- Provider warnings (Oracle `INSERT ALL` row-cap, unsupported `UpdateAll` on Oracle)

Use the left navigation to browse types alphabetically by namespace, or use the search box (top-right) to jump to a specific symbol.
