# SQLite

Package: `Inquiry.Sqlite`. Built on `Microsoft.Data.Sqlite`.

## Install

```bash
dotnet add package Inquiry.Sqlite
```

```csharp
[assembly: Inquiry.InquiryDialect("Sqlite")]
```

```csharp
services.AddInquirySqlite("Data Source=app.db");
```

## SQL flavor

| Aspect | Output |
|---|---|
| Identifier quoting | `"Quoted"` |
| Parameter prefix | `@name` |
| Auto-key | `INTEGER PRIMARY KEY AUTOINCREMENT` |
| Upsert | `INSERT … ON CONFLICT (…) DO UPDATE SET …` |
| Insert-returning | `INSERT … RETURNING …` (SQLite 3.35+) |
| Pagination | `LIMIT @limit OFFSET @offset` |
| Boolean | `INTEGER` (0/1) |
| String | `TEXT` |
| Soft-delete literal | `IsDeleted = 0` |
| JSON (`[InquiryJson]`) | Stored as `TEXT` (serialized text); JSON-path querying renders `json_extract("col", '$.path')` |
| IN binding | `col IN (SELECT value FROM json_each(@param))` (SQLite 3.38+) |
| Full-text-search | Not supported — `[InquiryFullTextSearch]` is a compile-time error (`INQ035`) |
| Update-returning | `UPDATE … RETURNING …` (SQLite 3.35+) |
| Upsert-returning | `INSERT … ON CONFLICT (…) DO UPDATE SET … RETURNING …` |

## Notes

- **In-memory test databases:** use `Data Source=:memory:` for a one-off, or `Data Source=Db_<guid>;Mode=Memory;Cache=Shared` plus a "keeper" connection for tests that need multiple connections to see the same data.
- **SequentialAccess is a no-op:** Microsoft.Data.Sqlite buffers rows regardless of the flag. Inquiry passes it anyway for parity; allocation numbers are determined by the buffering provider, not the flag.
- **Prepared statements:** the default `PreparedStatementMode.Auto` is a silent no-op for SQLite because prepared state is tied to the open connection and Inquiry opens a connection per operation.
- **No stored-procedure runtime:** SQLite has no native SP engine. SP-feature *generation* is exercised in `Inquiry.Generators.Tests`, but runtime SP integration tests run on the server dialects only.
- **Concurrency:** SQLite's default journal mode serializes writes. For high-concurrency tests, set `PRAGMA journal_mode = WAL;` on connection open.
- **No options overload or retry/failover:** SQLite is an embedded engine with no network layer — transient connection failures, backup-server failover, and connection pooling via `DbDataSource` do not apply. The `AddInquirySqlite` registration takes only a connection string (or `IConfiguration`); there is no options-lambda overload.

## Testing

`tests/Inquiry.Sqlite.Tests` runs end-to-end against in-memory SQLite — no Docker required. 100+ integration tests covering every feature.
