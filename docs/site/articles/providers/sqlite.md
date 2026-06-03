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

## Notes

- **In-memory test databases:** use `Data Source=:memory:` for a one-off, or `Data Source=Db_<guid>;Mode=Memory;Cache=Shared` plus a "keeper" connection for tests that need multiple connections to see the same data.
- **SequentialAccess is a no-op:** Microsoft.Data.Sqlite buffers rows regardless of the flag. Inquiry passes it anyway for parity; allocation numbers are determined by the buffering provider, not the flag.
- **No stored-procedure runtime:** SQLite has no native SP engine. SP-feature *generation* is exercised in `Inquiry.Generators.Tests`, but runtime SP integration tests run on the server dialects only.
- **Concurrency:** SQLite's default journal mode serializes writes. For high-concurrency tests, set `PRAGMA journal_mode = WAL;` on connection open.

## Testing

`tests/Inquiry.Sqlite.Tests` runs end-to-end against in-memory SQLite — no Docker required. 100+ integration tests covering every feature.
