# JSON columns

Store arbitrary structured data as JSON. Inquiry treats JSON columns as a special kind of value converter — your CLR type is `T`, the database type is `string` (a plain text column by default), and the System.Text.Json serializer bridges the two.

## You write

```csharp
public sealed class CustomerPreferences
{
    public string Theme { get; set; } = "light";
    public bool EmailOptIn { get; set; }
}

[InquiryTable("Customers")]
public sealed class Customer
{
    [InquiryKey] public string CustomerID { get; set; } = "";

    [InquiryColumn]
    [InquiryJson]
    public CustomerPreferences Preferences { get; set; } = new();
}
```

## Column type

By default an `[InquiryJson]` value is stored as the dialect's plain **string** column type — the generator maps the JSON converter's `string` provider type like any other text column, and emits **no** native `JSON`/`JSONB` type, `ISJSON`/`IS JSON` check, or `json_valid()` constraint:

| Dialect | Default emitted column type |
|---|---|
| PostgreSQL | `TEXT` |
| SQL Server | `NVARCHAR(MAX)` |
| MySQL | `LONGTEXT` (or `VARCHAR(n)` when a `Length` is set) |
| Sqlite | `TEXT` |
| Oracle | `CLOB` (or `VARCHAR2(n)` when a `Length` is set) |

To store the value in a provider's native JSON type, opt in with an explicit `SqlType` override and add any validating check constraint yourself. The override works on an `[InquiryJson]` property and on a plain `string` property that holds JSON text:

| Dialect | Opt-in | Behavior |
|---|---|---|
| PostgreSQL | `SqlType = "jsonb"` or `"json"` | PostgreSQL does not cast `text` to `json`/`jsonb` implicitly, so Inquiry binds these columns as `NpgsqlDbType.Jsonb` or `NpgsqlDbType.Json`. This applies to inserts, updates, upserts, batch writes, binary `COPY` bulk inserts, `[InquirySet]` assignments of a bare `@parameter`, and single-value predicates such as `Compare.Equal`. The match is case-insensitive. `Compare.In` and `Compare.NotIn` on the whole column are not supported: their values still bind as text, and PostgreSQL rejects the query. |
| MySQL | `SqlType = "JSON"` | The server validates and normalizes the document on write. A read returns the normalized text. |
| MariaDB | `SqlType = "JSON"` | `JSON` is an alias for `LONGTEXT` with a `JSON_VALID` check. |
| SQL Server | `SqlType = "JSON"` | Requires SQL Server 2025 or Azure SQL, which have the native `json` type. Earlier versions reject the type, so use `NVARCHAR(MAX)` with an `ISJSON` check there. |
| Oracle | `SqlType = "JSON"` | Requires Oracle 21c or later. |
| Sqlite | — | No native JSON type. Keep the default `TEXT` column. |

A native type can change the text that you read back: `jsonb` and MySQL `JSON` store a normalized document, so key order and whitespace can differ from what you wrote. An `[InquiryJson]` property is not affected, because it deserializes the value. A plain `string` property gets the normalized text.

Inside a longer `[InquirySet]` expression, such as `to_jsonb(@value)`, a parameter keeps its plain type, so the expression receives text.

The `::jsonb` cast used by [JSON-path querying](json-path-querying.md) is applied at query time and works against a text column too. It is not a DDL type. JSON-path criteria need a plain `string` property (an `[InquiryJson]` property is rejected with `INQ060`), and the path value always binds as text.

## The generator emits

The binder calls the column's converter `ToProvider` method and binds the resulting string. The materializer reads the string and calls `FromProvider`. Without a serializer context, the converter is the built-in `InquiryJsonConverter<T>`, which uses reflection-based `System.Text.Json` with default options. To customize serialization another way, supply a custom `IInquiryValueConverter` with `[InquiryColumn(Converter = typeof(MyJsonConverter))]`. An explicit `Converter` takes precedence over `[InquiryJson]`.

## Trimming and NativeAOT

The reflection-based converter is marked `RequiresUnreferencedCode` and `RequiresDynamicCode`. A trimmed or NativeAOT publish reports `IL2026` and `IL3050` for every `[InquiryJson]` property that uses it. To serialize through source-generated metadata instead, declare a `JsonSerializerContext` and select it for the assembly:

```csharp
using System.Text.Json.Serialization;
using Inquiry.Entities;

[assembly: InquiryJsonContext(typeof(AppJsonContext))]

[JsonSerializable(typeof(CustomerPreferences))]
internal partial class AppJsonContext : JsonSerializerContext;
```

Every `[InquiryJson]` property in the assembly now uses `AppJsonContext`. To use a different context for one property, set `Context`. It overrides the assembly default:

```csharp
[InquiryColumn]
[InquiryJson(Context = typeof(AuditJsonContext))]
public AuditTrail Audit { get; set; } = new();
```

Without either attribute, `[InquiryJson]` keeps the reflection-based converter, which stays the default for applications that do not trim.

### Rules

- **Metadata ownership.** Your application owns the context. Inquiry does not generate `System.Text.Json` metadata, because one source generator cannot read another's output. The context can live in a referenced assembly if the entity's assembly can access it.
- **Declare every column type.** The context needs `[JsonSerializable(typeof(T))]` for each JSON property type `T`. For a nullable value type `S?`, declare `S`. A type that the context reaches only through another type's properties does not count. A missing type is build error [INQ110](../diagnostics.md#inq110), and a context that is not a `JsonSerializerContext` is [INQ108](../diagnostics.md#inq108).
- **No reflection fallback.** If the context has no metadata for the type at run time, reads and writes throw `InvalidOperationException` that names the context and the type. This can happen when a context in another assembly changes without a rebuild.
- **Type support.** Inquiry checks that the context declares the type. System.Text.Json decides whether it can serialize the type. An unsupported type, such as a multidimensional array, throws `NotSupportedException` from System.Text.Json on first use, as it does with the reflection-based converter.
- **Deserialization metadata.** `JsonSourceGenerationMode.Serialization` alone generates no metadata for reads, so Inquiry reports `INQ110` for it. Keep the default mode or `Metadata`.

### Serializer options

Generated code uses the context's generated `Default` instance, so serializer options come only from `[JsonSourceGenerationOptions]` on the context. Inquiry does not use a `JsonSerializerOptions` instance that you build at run time, or converters that you add to one. With no options attribute, a context writes the same text as the reflection-based converter for the types it declares, so you can switch an existing column to a context. A value whose runtime type differs from the declared type, such as an `object` property, also needs metadata or `[JsonDerivedType]` polymorphism for that runtime type. Options such as a naming policy change the stored text. Rows written under the old options must still deserialize under the new ones.

### Provider storage

Both converters read and write a `string`. The context changes only how the value is serialized. Column types, the native `SqlType` opt-ins, PostgreSQL `json` and `jsonb` binding, and normalization on read all behave as described in [Column type](#column-type). A null property value is stored as SQL `NULL`. If a column holds the JSON literal `null` and it deserializes to a CLR `null`, the read throws `InvalidOperationException` with either converter. Types such as `JsonElement` deserialize it to a non-null value instead.

The [NativeAOT compatibility](../aot-compatibility.md) page lists the supported combinations.

## See also

- [JSON-path querying](json-path-querying.md) — filter *inside* a JSON text column from a predicate method (`[InquiryWhere(JsonPath = "$.a.b")]`).
