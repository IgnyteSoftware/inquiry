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

To store the value in a provider's native JSON type, opt in with an explicit override — `[InquiryColumn(SqlType = "JSONB")]` on PostgreSQL, `[InquiryColumn(SqlType = "JSON")]` on MySQL / SQL Server / Oracle — and add any validating check constraint yourself. (The `::jsonb` cast used by [JSON-path querying](json-path-querying.md) is applied at query time and works against a text column too — it is not a DDL type.)

## The generator emits

The binder writes `System.Text.Json.JsonSerializer.Serialize(_e.Preferences)`; the materializer calls `JsonSerializer.Deserialize<CustomerPreferences>(reader.GetString(i))`. To customize serialization (or for AOT/trim safety), supply a custom `IInquiryValueConverter` via `[InquiryColumn(Converter = typeof(MyJsonConverter))]` backed by a source-generated `JsonSerializerContext`.

## See also

- [JSON-path querying](json-path-querying.md) — filter *inside* a JSON text column from a predicate method (`[InquiryWhere(JsonPath = "$.a.b")]`).
