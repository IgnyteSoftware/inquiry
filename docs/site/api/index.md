# API reference

Auto-generated from the XML doc comments on the public Inquiry packages.

## Namespaces

| Namespace | What lives here |
|---|---|
| `Inquiry` | The `IInquiry` facade, attributes (`InquiryTable`, `InquiryColumn`, etc.), `InquiryStore<T>` base class, options, dialect marker. |
| `Inquiry.Commands` | `InquiryCommand`, command contexts, and generated-support command-resource cleanup for custom pipelines. |
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
| `Inquiry.SqlServer.Parameters` | Generated-support TVP binding metadata. `InquiryTvpDescriptor.Get` returns the cached descriptor for an exact generator-resolved physical signature. |
| `Inquiry.PostgreSql` | PostgreSQL provider package: public DI extension/options, internal connection factory. |
| `Inquiry.MySql` | MySQL / MariaDB provider package: public DI extension, internal connection factory. |
| `Inquiry.Oracle` | Oracle provider package: public DI extension, internal connection factory. |

## Diagnostics

Inquiry emits `INQxxx` diagnostics at compile time. Each is documented in the source on `InquiryDiagnosticDescriptors`; they cover:

- Entity-shape errors (missing key, duplicate columns, unsupported types)
- Store-method errors (unknown column in `[InquiryWhere]`, unsupported return shape, conflicting attributes)
- Provider warnings (Oracle `INSERT ALL` row-cap, unsupported `UpdateAll` on Oracle)
- Provider artifact errors, including `INQ076` when a SQL Server collection cannot be mapped atomically to one exact, compatible TVP physical signature. The diagnostic points to the conflicting `SqlType`, length, Unicode, precision, or scale facet where possible; the generator emits no deployable artifact and the affected method is an unreachable throw stub.

Use the left navigation to browse types alphabetically by namespace, or use the search box (top-right) to jump to a specific symbol.

## Generated-support lifetime contract

Generated SQL Server TVP bindings may retain a single-pass source enumerator until command execution finishes. Built-in Inquiry pipelines release the reader, retained binder resources, command, and owned connection in that order and aggregate cleanup failures after the primary operation failure. Custom pipelines and direct binder callers must invoke `Inquiry.Commands.InquiryCommandResources.Dispose(dbCommand)` before disposing the command or connection. If execution and cleanup both fail, the execution failure must remain first and cleanup failures follow in disposal order; a `finally` cleanup must not replace the operation failure. `Dispose` is idempotent after a command's resources have been detached, but it can throw when a retained resource fails to dispose.
