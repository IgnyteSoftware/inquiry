# Project status

**Inquiry is a compile-time-SQL micro-ORM** — a Roslyn incremental source generator that bakes every SQL
statement as a `const string` at build time. The runtime ships zero SQL.

**Last reconciled against the code:** 2026-06-06.

## Supported database engines (5, all live-tested)

| Dialect (`[assembly: InquiryDialect("…")]`) | Runtime package | Analyzer (source generator) | Live test status |
|---|---|---|---|
| `Sqlite` | `Inquiry.Sqlite` | `Inquiry.Sqlite.Analyzer` | in-process (no Docker) |
| `SqlServer` | `Inquiry.SqlServer` | `Inquiry.SqlServer.Analyzer` | Testcontainers (CI integration matrix) |
| `PostgreSql` | `Inquiry.PostgreSql` | `Inquiry.PostgreSql.Analyzer` | Testcontainers (CI integration matrix) |
| `MySql` | `Inquiry.MySql` | `Inquiry.MySql.Analyzer` | Testcontainers (CI integration matrix) |
| `Oracle` | `Inquiry.Oracle` | `Inquiry.Oracle.Analyzer` | Testcontainers (CI integration matrix) |

The shared generator framework lives in `Inquiry.Generators.Shared` and is bundled privately into each
`*.Analyzer` (Roslyn loads each analyzer in its own `AssemblyLoadContext`, so the framework cannot be a
shared analyzer dependency). See [Design notes](design-notes.md) for the architecture.

## Target frameworks

The core `Inquiry` runtime and the test projects target **net8.0; net9.0; net10.0** — the floor is
**.NET 8**. The provider runtime libraries target **net8.0**. (EOL net6.0/net7.0 were dropped.)

## Feature completeness

The original 13-workstream feature roadmap — MySQL & Oracle providers, cloud-compat modes, richer WHERE
predicates, ORDER BY + offset/keyset pagination, batch & bulk operations, automatic prepared-statement
reuse, projections + aggregations, optimistic concurrency, schema-DDL generation, soft deletes, full-text
search, and JSON/array/value-converter columns — is **implemented and merged to `main`**. The per-workstream
design record is in [Design notes](design-notes.md); user-facing docs for each feature are under
[Features](../articles/features/crud.md).

Remaining follow-ups (and explicitly out-of-scope items) are tracked on the [Roadmap](roadmap.md).

## Security status

A formal Codex Security repository scan was completed during pre-release hardening. The validated findings
were fixed on `main` in `318ee5f` (`Fix security scan findings`): lazy batch materialization now enforces
the parameter cap while enumerating, MySQL update-returning on concurrency-token rows no longer returns stale
rows after a failed update, and Oracle generated bind names no longer collapse leading-underscore parameters.

## Test status

Tests are organized per concern; the non-Docker suites always run, and the provider suites use
Testcontainers. Local provider runs **skip gracefully when Docker is unavailable**; CI sets
`INQUIRY_REQUIRE_DOCKER=1`, so a missing or failed provider container fails the integration job.

| Suite | Scope | Needs Docker? |
|---|---|---|
| `Inquiry.Generators.Tests` | source-generator emission + per-dialect SQL assertions | no |
| `Inquiry.Tests` | runtime pipeline, parameter binding, transactions | no |
| `Inquiry.Sqlite.Tests` | in-process end-to-end CRUD/eager + schema fidelity | no |
| `Inquiry.PostgreSql.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.SqlServer.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.MySql.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.Oracle.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.IntegrationTesting` | shared schema-fidelity comparator + introspection support | n/a (library) |

Every live dialect exercises the full supported feature set via a shared, linked feature catalog
(versioned/soft-delete/JSON/full-text entities) plus aggregate/projection and batch methods on the
Northwind stores.

**For current test counts**, run the whole suite (`dotnet test`) or a single project
(e.g. `dotnet test tests/Inquiry.MySql.Tests -f net8.0`). All suites are green on `main`; Docker-gated
suites skip (not fail) without Docker.
