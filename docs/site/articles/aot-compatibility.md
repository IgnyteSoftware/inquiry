# NativeAOT compatibility

This page lists which Inquiry features work in trimmed and NativeAOT applications, and how each
combination is verified. "Supported" means the feature produces no trimming or AOT warnings
(`IL2026`, `IL3050`) in your publish. "Not supported" means the API is marked
`RequiresUnreferencedCode` or `RequiresDynamicCode`, so a trimmed or NativeAOT publish reports those
warnings.

## Features

| Feature | Trimming and NativeAOT | Verification |
|---|---|---|
| Generated stores, materializers and parameter binding | Supported | The `aot-smoke` CI job publishes and runs SQLite CRUD as a native binary. The package consumer check repeats it with the candidate packages. |
| Generated registration, `AddInquiryGeneratedStores()` | Supported | Same as above. |
| Assembly-scanning registration, `AddInquiry(params Assembly[])` | Not supported | Marked `RequiresUnreferencedCode`. Use `AddInquiryGeneratedStores()`. |
| `[InquiryJson]` with a `JsonSerializerContext` from `[assembly: InquiryJsonContext]` | Supported | `aot-smoke` round-trips a structured value and a null value. |
| `[InquiryJson(Context = typeof(...))]` per-property context | Supported | `aot-smoke` round-trips a dictionary through a second context with its own options. |
| `[InquiryJson]` without a context | Not supported | `InquiryJsonConverter<T>` uses reflection-based `System.Text.Json`. |
| Custom `IInquiryValueConverter` through `[InquiryColumn(Converter = typeof(...))]` | Supported when the converter itself is trim-safe | Not executed by Inquiry. Your converter's code decides. |

See [JSON columns](features/json-columns.md#trimming-and-nativeaot) for the context rules, serializer
options and the build errors for missing metadata.

## Providers

Every Inquiry runtime package sets `IsAotCompatible`, so its own code builds with the trimming and AOT
analyzers. The ADO.NET driver that each provider uses has its own NativeAOT support, which Inquiry does
not verify.

| Provider | JSON column storage | Executed as a NativeAOT binary by Inquiry |
|---|---|---|
| SQLite | `TEXT` | Yes: `aot-smoke` and the package consumer check |
| PostgreSQL | `TEXT`, or `json`/`jsonb` bound as `NpgsqlDbType.Json`/`Jsonb` | No |
| SQL Server | `NVARCHAR(MAX)`, or `JSON` on SQL Server 2025 and Azure SQL | No |
| MySQL | `LONGTEXT`, or `JSON` | No |
| MariaDB | `LONGTEXT`, or `JSON` (an alias for `LONGTEXT` with a check) | No |
| Oracle | `CLOB`, or `JSON` on Oracle 21c and later | No |

The serializer context changes only how a value becomes text. It does not change the column type or
how the provider binds it. [JSON columns](features/json-columns.md#column-type) describes each
provider's storage.
