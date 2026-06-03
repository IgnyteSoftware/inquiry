# Full-text search

Inquiry exposes the native full-text-search facility of each provider behind a single `[InquiryFullTextSearch]` attribute. The SQL the generator emits is provider-specific; the C# call shape is uniform.

## You write

```csharp
public partial class ArticleStore : InquiryStore<Article>
{
    [InquiryFullTextSearch("Title", "Body")]
    public partial Task<IReadOnlyList<Article>> SearchAsync(string query, CancellationToken ct = default);
}
```

## The generator emits

Per dialect:

| Dialect | Generated `WHERE` clause |
|---|---|
| SQL Server | `WHERE CONTAINS(([Title],[Body]), @query)` (requires a full-text catalog + index) |
| PostgreSQL | `WHERE to_tsvector('english', "Title" \|\| ' ' \|\| "Body") @@ plainto_tsquery('english', @query)` |
| MySQL | `WHERE MATCH(`Title`,`Body`) AGAINST (@query IN NATURAL LANGUAGE MODE)` (requires a FULLTEXT index) |
| SQLite | Requires the FTS5 virtual-table pattern — see SQLite notes. |
| Oracle | `WHERE CONTAINS("Title" \|\| ' ' \|\| "Body", :query) > 0` (requires an Oracle Text index) |

You provide the index / catalog in your schema setup; Inquiry emits the query.
