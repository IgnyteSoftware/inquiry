# Inquiry

Inquiry is an experimental **.NET 8+** source-generated micro-ORM. You write attributed entity classes and `partial` store classes with `partial` method declarations; a Roslyn source generator emits the matching partial with method bodies, materializers, and dependency-injection wiring. Every SQL string is built at compile time by a provider-specific `SqlBuilder` and baked into the generated source as `const string` fields, so each database can be tuned independently and **the runtime carries no SQL**.

> **Documentation lives in the DocFX site** ([`docs/site/`](docs/site/)) — getting-started, features, per-dialect notes, the architecture deep-dive, security, and an auto-generated API reference, plus a **Develop** area with project status and the roadmap. The site is local-preview only for now: run `docfx docs/site/docfx.json --serve` from the repo root (see [`docs/site/README.md`](docs/site/README.md)).
>
> - **New here?** [Getting started](docs/site/articles/getting-started.md) · [How it works](docs/site/articles/concepts.md)
> - **Reference:** [Features](docs/site/articles/features/crud.md) · [Providers](docs/site/articles/providers/sqlite.md) · [Security](docs/site/articles/security.md) · [Architecture](docs/site/articles/architecture.md)
> - **Project state:** [Project status](docs/site/develop/project-status.md) · [Roadmap](docs/site/develop/roadmap.md) · [Contributing](docs/site/develop/contributing.md)

## Repository layout

| Project | Purpose |
| --- | --- |
| `src/Inquiry` | Public runtime: `IInquiry` facade, attributes, command/parameter types, transactions, options, and the DI extension `AddInquiry()`. Ships no SQL; the request pipeline is internal. |
| `src/Inquiry.Generators.Shared` | Roslyn incremental source-generator framework. Discovers entities and stores; emits materializers, generated stores, the DI registration class, and `InquiryGeneratedSchema.Ddl`. Owns the per-dialect `SqlBuilder` hierarchy. Bundled privately into each provider analyzer. |
| `src/Inquiry.{Sqlite,SqlServer,PostgreSql,MySql,Oracle}.Analyzer` | Per-dialect Roslyn analyzers — each a `[Generator]` that bundles the shared framework and emits only when its dialect matches the resolved `[InquiryDialect]`. |
| `src/Inquiry.{Sqlite,SqlServer,PostgreSql,MySql,Oracle}` | Per-dialect runtime providers: `AddInquiry<Dialect>(...)` DI extension, provider options, internal connection factory, and the `[assembly: InquiryDialect("...")]` marker. |
| `tests/…` | Core runtime tests, source-generator tests, the shared `Inquiry.IntegrationTesting` support library, and per-dialect end-to-end suites (SQLite in-process; the rest via Testcontainers). |
| `samples/Inquiry.Northwind` | Shared classic-Northwind entities, stores, and per-provider DDL consumed by the samples and integration tests. |
| `samples/Inquiry.Sample` | Runnable ASP.NET Core sample exercising CRUD, upsert, transactions, and eager loading on SQLite. |

## Quickstart

```csharp
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryTable("TOrganization")]
public sealed class Organization
{
    [InquiryKey] public Guid Key { get; set; } = Guid.NewGuid();
    [InquiryColumn("Name")] public string Name { get; set; } = string.Empty;
    [InquiryColumn] public bool IsActive { get; set; } = true;
}

public partial class OrganizationStore : InquiryStore<Organization>
{
    [InquirySelectAll]
    public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct = default);

    [InquiryInsert]
    public partial Task<int> InsertAsync(Organization o, CancellationToken ct = default);
}
```

Register Inquiry with a provider and resolve the store:

```csharp
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;

services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

var orgs = sp.GetRequiredService<OrganizationStore>();
await foreach (var o in orgs.SelectAllAsync()) { /* ... */ }
```

The method bodies, the SQL `const string`s, the materializers, and the DI wiring are all generated at build time. Beyond this core CRUD surface, Inquiry supports richer WHERE predicates, ORDER BY + offset/keyset pagination, batch & bulk operations, projections + aggregations, optimistic concurrency, soft deletes, full-text search, JSON/value-converter columns, `CREATE TABLE` schema-DDL generation, opt-in observability (OpenTelemetry tracing + metrics and `ILogger` logging via `AddInquiryTelemetry()`), and open-time resiliency (cloud transient retry + backup-server failover). See [How it works](docs/site/articles/concepts.md) and the [Architecture deep-dive](docs/site/articles/architecture.md) for the full compile-time pipeline, SQL-building, and runtime walkthrough.

## Running the sample

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The sample seeds an in-process SQLite database, exposes a small HTML dashboard at `/`, and a handful of JSON endpoints under `/api/...` that exercise CRUD, upsert, eager loading, and a transactional insert.

## Running the tests

```powershell
dotnet test
```

Tests cover parameter binding, the request pipeline, transactions, generator emission, per-dialect SQL strings, end-to-end CRUD/eager-loading against in-memory SQLite, and — for every provider — live CRUD, schema-fidelity, and generated-DDL verification against the real engine.

The SQL Server, PostgreSQL, MySQL, and Oracle integration suites provision their engine with **[Testcontainers](https://dotnet.testcontainers.org/)** — the only host dependency is **Docker**. When Docker is unavailable every live fact **skips** (via `SkippableFact`) rather than failing, so `dotnet test` stays green without Docker. CI runs PostgreSQL, MySQL, SQL Server, and Oracle on pushes to `main` (and on the `pull_request` event if a PR is opened). See [Project status](docs/site/develop/project-status.md) for the current state and the [Roadmap](docs/site/develop/roadmap.md) for what's next.
