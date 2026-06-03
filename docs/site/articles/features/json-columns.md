# JSON columns

Store arbitrary structured data as JSON. Inquiry treats JSON columns as a special kind of value converter — your CLR type is `T`, the database type is `string` (or the provider's native JSON type), and the System.Text.Json serializer bridges the two.

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

## Provider-specific column types

| Dialect | JSON column type |
|---|---|
| PostgreSQL | `JSONB` (or `JSON` for opt-in) |
| SQL Server | `NVARCHAR(MAX)` with `ISJSON` check constraint (2016+) or native `JSON` (2025+) |
| MySQL | `JSON` |
| Sqlite | `TEXT` (validated via `json_valid()` if you add the constraint) |
| Oracle | `JSON` (21c+) or `CLOB` with `IS JSON` check |

## The generator emits

The binder writes `System.Text.Json.JsonSerializer.Serialize(_e.Preferences)`; the materializer calls `JsonSerializer.Deserialize<CustomerPreferences>(reader.GetString(i))`. Serializer options can be customized per-property via `[InquiryJson(SerializerOptions = ...)]`.
