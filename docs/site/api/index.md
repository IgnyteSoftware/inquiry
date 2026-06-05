# API reference

Auto-generated from the XML doc comments on the public Inquiry packages.

## Namespaces

| Namespace | What lives here |
|---|---|
| `Inquiry` | The `IInquiry` facade, attributes (`InquiryTable`, `InquiryColumn`, etc.), `InquiryStore<T>` base class, options, dialect marker. |
| `Inquiry.Commands` | `InquiryCommand`, `InquiryCommandContext`, executed/failed contexts. |
| `Inquiry.Connections` | `IInquiryConnectionFactory` for custom provider integration. Built-in retry helpers are internal. |
| `Inquiry.Converters` | Generated-code support for built-in value converters. |
| `Inquiry.DependencyInjection` | DI extension methods such as `AddInquiry`; generated stores emit their own `AddInquiryGeneratedStores`. |
| `Inquiry.Entities` | Entity-shape attributes (`InquiryTable`, `InquiryKey`, `InquiryColumn`, navigations, indexes). |
| `Inquiry.Interceptors` | `IInquiryCommandInterceptor` and the context types passed to it. |
| `Inquiry.Materialization` | Generated-code support for `IInquiryEntityMaterializer<T>`. Hidden from IntelliSense for application users. |
| `Inquiry.Parameters` | `InquiryParameter` plus generated-code support for bounded `IN` expansion. |
| `Inquiry.Stores` | Store-method operation attributes (`InquirySelectAll`, `InquiryInsert`, `InquiryStoredProcedure`, etc.) and the `InquiryStore<T>` base. |
| `Inquiry.Transactions` | `IInquiryTransaction` for explicit transaction handles and nested savepoints. |
| `Inquiry.Sqlite` | SQLite provider package: public DI extension, internal connection factory. |
| `Inquiry.SqlServer` | SQL Server provider package: public DI extension/options, internal connection factory. |
| `Inquiry.PostgreSql` | PostgreSQL provider package: public DI extension/options, internal connection factory. |
| `Inquiry.MySql` | MySQL / MariaDB provider package: public DI extension, internal connection factory. |
| `Inquiry.Oracle` | Oracle provider package: public DI extension, internal connection factory. |

## Diagnostics

Inquiry emits `INQ001`–`INQ040` diagnostics at compile time. Each is documented in the source on `InquiryDiagnosticDescriptors`; they cover:

- Entity-shape errors (missing key, duplicate columns, unsupported types)
- Store-method errors (unknown column in `[InquiryWhere]`, unsupported return shape, conflicting attributes)
- Provider warnings (Oracle `INSERT ALL` row-cap, unsupported `UpdateAll` on Oracle)

Use the left navigation to browse types alphabetically by namespace, or use the search box (top-right) to jump to a specific symbol.
