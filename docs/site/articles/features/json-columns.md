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

The binder writes `System.Text.Json.JsonSerializer.Serialize(_e.Preferences)`; the materializer calls `JsonSerializer.Deserialize<CustomerPreferences>(reader.GetString(i))`. To customize serialization (or for AOT/trim safety), supply a custom `IInquiryValueConverter` via `[InquiryColumn(Converter = typeof(MyJsonConverter))]` backed by a source-generated `JsonSerializerContext`.

## See also

- [JSON-path querying](json-path-querying.md) — filter *inside* a JSON text column from a predicate method (`[InquiryWhere(JsonPath = "$.a.b")]`).
