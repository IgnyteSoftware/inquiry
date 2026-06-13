# JSON-path querying

Filter *inside* a JSON column from a predicate method. Add a `JsonPath` to an `[InquiryWhere]` criterion and the generator compares the dialect's JSON extraction of that path against the bound parameter — `WHERE json_extract("Data", '$.status') = @status` — instead of comparing the whole column. This is the EF Core JSON-query parity (`HasColumnType("jsonb")` + LINQ into the path) over Inquiry's compile-time predicate model.

## You write

```csharp
[InquiryTable("Catalog")]
public sealed class CatalogItem
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = "";

    // A plain string column holding JSON text (e.g. {"status":"active","address":{"city":"Boston"}}).
    [InquiryColumn] public string Data { get; set; } = "";
}

public partial class CatalogStore : InquiryStore<CatalogItem>
{
    // WHERE json_extract("Data", '$.status') = @status
    [InquirySelectAllByPredicate]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.status")]
    public partial Task<IReadOnlyList<CatalogItem>> ByStatusAsync(string status, CancellationToken ct = default);

    // Nested path, AND-composed with an ordinary criterion.
    [InquirySelectAllByPredicate]
    [InquiryWhere("Name", Compare.Like)]
    [InquiryWhere("Data", Compare.Equal, JsonPath = "$.address.city")]
    public partial Task<IReadOnlyList<CatalogItem>> SearchAsync(string name, string city, CancellationToken ct = default);
}
```

`JsonPath` works on any `[InquiryWhere]` criterion, so it composes with AND/OR, the other operators (`Like`, `In`, `IsNull`, …), and the active-row / soft-delete filters exactly like an ordinary criterion. The bound parameter binds positionally just like every other predicate parameter; the generated parameter name is taken from the path's leaf segment (`$.address.city` → `@city`).

## The generator emits

The extraction is per-dialect — the path you write (`$.a.b`) is the SQL/JSON-path form that four of the five dialects take verbatim; PostgreSQL uses its `#>>` text-path operator, so the path is translated to `{a,b}` and the column cast to `jsonb`.

| Dialect | Emitted extraction |
|---|---|
| Sqlite | `json_extract("Data", '$.status')` |
| SQL Server | `JSON_VALUE([Data], '$.status')` |
| MySQL | `JSON_UNQUOTE(JSON_EXTRACT(\`Data\`, '$.status'))` |
| Oracle | `JSON_VALUE(Data, '$.status')` |
| PostgreSQL | `("Data")::jsonb #>> '{address,city}'` |

All five extract the value **as text**, so the comparison is textual and the bound parameter is a `string`.

## Rules & scope (v1)

- **The field must be a plain `string` column** holding JSON text — not an `[InquiryJson]`-converted column. The comparison value binds as raw text, so a column with a value converter (which would serialize the value as JSON) is rejected with **`INQ060`**. To query into JSON, map the column as `string`; you can still read/write it as a typed object through a separate `[InquiryJson]` property if you keep both, or parse it in app code.
- **The path must be a well-formed dotted object path** — `$` then one or more `.segment`, where each segment is letters, digits, `_` or `-` (so `$.address.city` and `$.first-name` are fine). Anything else — a bare `$`, a trailing/empty segment, an array index (`$.a[0]`), a quoted or space-bearing key — is rejected with **`INQ060`**. The strict grammar keeps the path safe to embed in the generated SQL on every dialect and uniformly translatable; array indices and exotic keys are a future addition rather than something silently mis-handled.
- **Comparisons are textual.** Extracted values compare as strings, so the parameter is a `string` and ordering (`>`/`<`) is lexicographic. Equality, `Like`, `In`, and `IsNull`/`IsNotNull` on text values are the intended uses; typed numeric/boolean path comparison (with a cast) is a future addition.
- **PostgreSQL requires valid JSON in every row.** The PostgreSQL extraction casts the column to `jsonb` (`("Data")::jsonb #>> '{…}'`), which **errors on a row whose text is not valid JSON** (including an empty string) — and that aborts the whole query, not just the row. Ensure every value is valid JSON or `NULL`; for a column dedicated to JSON, declare it `[InquiryColumn(SqlType = "jsonb")]` so the store is `jsonb` natively (the cast is then a no-op) and the database rejects bad writes up front. The other dialects' extraction functions are lenient and return `NULL` for a non-matching or invalid document.

## See also

- [JSON columns](json-columns.md) — storing structured data as JSON with `[InquiryJson]`.
- [CRUD](crud.md#predicate-queries) — the `[InquiryWhere]` predicate model this extends.
- [Soft delete](soft-delete.md) / [Global query filters](global-filters.md) — the active-row filters a JSON-path criterion composes with.
