# Project status

**Inquiry is a compile-time-SQL micro-ORM** — a Roslyn incremental source generator that bakes every SQL
statement as a `const string` at build time. The runtime ships zero SQL.

**Last reconciled against the code:** 2026-07-13 from the MySQL-family restoration branch based on
`331a478`.

## Supported database engines (6, all live-tested)

| Dialect (`[assembly: InquiryDialect("…")]`) | Runtime package | Analyzer (source generator) | Live test status |
|---|---|---|---|
| `Sqlite` | `Inquiry.Sqlite` | `Inquiry.Sqlite.Analyzer` | in-process (no Docker) |
| `SqlServer` | `Inquiry.SqlServer` | `Inquiry.SqlServer.Analyzer` | Testcontainers (CI integration matrix) |
| `PostgreSql` | `Inquiry.PostgreSql` | `Inquiry.PostgreSql.Analyzer` | Testcontainers (CI integration matrix) |
| `MySql` | `Inquiry.MySql` | `Inquiry.MySql.Analyzer` | Testcontainers (CI integration matrix) |
| `MariaDb` | `Inquiry.MariaDb` | `Inquiry.MariaDb.Analyzer` | Testcontainers (CI integration matrix) |
| `Oracle` | `Inquiry.Oracle` | `Inquiry.Oracle.Analyzer` | Testcontainers (CI integration matrix) |

The shared generator framework lives in `Inquiry.Generators.Shared` and is bundled privately into each
`*.Analyzer` (Roslyn loads each analyzer in its own `AssemblyLoadContext`, so the framework cannot be a
shared analyzer dependency). See [Design notes](design-notes.md) for the architecture.

## Target frameworks

All shipped packages — the core `Inquiry` runtime, the provider runtime libraries, and the companion
packages — target **net8.0; net9.0; net10.0**, as do the test projects; the floor is **.NET 8**.
(EOL net6.0/net7.0 were dropped.)

## Feature completeness

The original 13-workstream implementation roadmap — MySQL & Oracle providers, cloud-compat modes, richer WHERE
predicates, ORDER BY + offset/keyset pagination, batch & bulk operations, automatic prepared-statement
reuse, projections + aggregations, optimistic concurrency, schema-DDL generation, soft deletes, full-text
search, and JSON/array/value-converter columns — has an initial implementation merged to `main`. That does
**not** mean the library is 1.0-complete: live integration coverage has exposed correctness and performance
follow-ups, and the first-release gates are tracked on the [Roadmap](roadmap.md). The per-workstream
design record is in [Design notes](design-notes.md); user-facing docs for each feature are under
[Features](../articles/features/crud.md).

Beyond that roadmap, the runtime also ships an opt-in **observability layer** (OpenTelemetry tracing +
metrics + `ILogger` logging via `AddInquiryTelemetry()`; see
[Observability](../articles/features/observability.md)) and **open-time resiliency** (cloud transient-fault
retry and per-provider backup-server failover; see
[Resiliency & failover](../articles/features/resiliency.md)).

Remaining follow-ups (and explicitly out-of-scope items) are tracked on the [Roadmap](roadmap.md).

## Release engineering

Packages are versioned by [MinVer](https://github.com/adamralph/minver) from git tags. No public release has
shipped yet; the first release will use the `v1.0.0` tag and package version `1.0.0`. Every package embeds
SourceLink metadata (`Microsoft.SourceLink.GitHub`) and ships a `.snupkg` symbol package, including the
provider analyzer PDBs. The verifier binds each PDB to its DLL CodeView identity and checks complete
SourceLink document coverage at the exact commit. The previous
tag-triggered rebuild-and-wildcard-push workflow was removed because it did not prove the complete provider
or package-consumer gates and could not safely recover from partial publication. `eng/release-manifest.json`
now defines the exact nine-package 1.0 bundle, and the cross-platform verifier rejects inventory, version,
dependency, repository-commit, metadata, symbol, and SourceLink drift. Public publishing remains disabled
until the immutable RC, independent verification, protected promotion, and resumable publisher stages of
[#89](https://github.com/JakeOverstreet/inquiry/issues/89) land. See [Contributing — Releasing](contributing.md#releasing).

## Security status

A formal Codex Security repository scan was completed during early pre-release hardening. The validated findings
were fixed on `main` in `318ee5f` (`Fix security scan findings`): lazy batch materialization now enforces
the parameter cap while enumerating, MySQL update-returning on concurrency-token rows no longer returns stale
rows after a failed update, and Oracle generated bind names no longer collapse leading-underscore parameters.
The codebase has changed substantially since that scan; a fresh release-candidate scan and threat-model review
are required by [#89](https://github.com/JakeOverstreet/inquiry/issues/89).

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
| `Inquiry.MariaDb.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.Oracle.Tests` | live Northwind + generated-DDL + feature-matrix | yes |
| `Inquiry.IntegrationTesting` | shared schema-fidelity comparator + introspection support | n/a (library) |
| `Inquiry.FeatureCatalog` | shared feature-catalog entities, stores, and helpers linked into each provider suite | n/a (library) |

Every live dialect exercises the full supported feature set via a shared, linked feature catalog
(versioned/soft-delete/JSON/full-text entities) plus aggregate/projection and batch methods on the
Northwind stores.

**For current test counts**, run the whole suite (`dotnet test`) or a single project
(e.g. `dotnet test tests/Inquiry.MySql.Tests -f net8.0`). The current release gate and exact run evidence
are tracked in [#171](https://github.com/JakeOverstreet/inquiry/issues/171). PostgreSQL is green at
253/253 on each supported TFM. SQL Server is green at 298/298 on each TFM plus a fresh net10 repeat,
with zero skips using the release-gating FTS image. MySQL is green at 255/255 on each TFM plus a fresh
net10 repeat; MariaDB is green at 258/258 on each TFM plus a fresh net10 repeat. Oracle and consecutive
full-CI evidence remain. Docker-gated suites skip locally (not in CI) when Docker is unavailable.

The normal CI workflow runs on pull requests targeting `prerelease` and `main`, and on merge-queue events:
**build-and-unit**
(generator, runtime, and SQLite suites — no Docker), **aot-smoke** (publishes and runs the NativeAOT
smoke app), and an **integration** matrix (PostgreSQL, MySQL, MariaDB, SQL Server, Oracle ×
net8.0/net9.0/net10.0 via Testcontainers — exactly 15 required legs). The `ci-required-v1` aggregator runs even after failures and fails unless
all required jobs and matrix legs succeed. Direct merging has been retired; external rulesets must protect
both branches with this context and the review requirements documented under
[#89](https://github.com/JakeOverstreet/inquiry/issues/89). A separate **scheduled weekly workflow**
(`scheduled.yml`) repeats the full provider × net8.0/net9.0/net10.0 matrix every Monday.
