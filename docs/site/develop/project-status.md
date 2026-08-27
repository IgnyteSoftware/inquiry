# Project status

**Inquiry is a compile-time-SQL micro-ORM** — a Roslyn incremental source generator that bakes every SQL
statement as a `const string` at build time. The runtime ships zero SQL.

**Last reconciled against the code and GitHub:** 2026-08-18 (governance and release-engineering
claims; issue counts were last reconciled 2026-08-01).

**1.0.0 is not release-ready**, though the stop-ship lane is now clear. GitHub has 61 open issues: 12 are
assigned to the `1.0.0` milestone (6 P1, 5 P2, and one unlabelled eager-grid performance gap, #265); five
are explicitly planned for 1.x or demand-driven work; and 44 are non-blocking `review-gate` follow-ups
(#242–#288). **No P0 remains**
— [#89](https://github.com/IgnyteSoftware/inquiry/issues/89) (release engineering and governance) closed
2026-07-25. The [Roadmap](roadmap.md) records the complete priority inventory.

Delivery is active. Since 2026-07-14, thirteen prioritized issues have closed: #214 (SequentialGuid sort
order), #78 (stored-procedure INOUT / multi-result-set / Oracle REF CURSOR), #156 (end-to-end
cancellation), #178 (pessimistic row-level locking), #188 (stored-procedure metadata), #210 (column-list
constants), #211 (InquiryPagedResult), #212 (typed FK attribute), #213 (ASP.NET Core audit-context
middleware), #219 (generated query contracts),
[#69](https://github.com/IgnyteSoftware/inquiry/issues/69) (SQL Server TVP production hardening),
[#87](https://github.com/IgnyteSoftware/inquiry/issues/87) (benchmark truth and regression gates), and
[#86](https://github.com/IgnyteSoftware/inquiry/issues/86) (unified telemetry for all execution paths).
[#89](https://github.com/IgnyteSoftware/inquiry/issues/89) (release engineering and governance) closed
2026-07-25: governance docs (SECURITY.md, SUPPORT.md, CHANGELOG.md), public API baseline
(PublicApiAnalyzers + EnablePackageValidation), package verification, CycloneDX SBOM and SLSA build
provenance, hosted docs, CODEOWNERS and branch protection, and CodeQL plus dependency scanning. The repository is now public
under the IgnyteSoftware organization, so branch-protection rulesets
(`eng/configure-branch-protection.ps1`) and CodeQL code scanning are both active.
[#70](https://github.com/IgnyteSoftware/inquiry/issues/70) (eager loading) closed 2026-07-26, and
[#80](https://github.com/IgnyteSoftware/inquiry/issues/80) (many-to-many: auto-managed junction,
composite keys, child filters on eager M:N) closed 2026-07-31.
[#82](https://github.com/IgnyteSoftware/inquiry/issues/82) (query filters) is **complete** as of
2026-08-02: named filters with a per-method bypass and runtime-parameterized filters shipped first,
then opt-in write-side enforcement on key-based writes (`EnforceOnWrites`, INQ095) and the PostgreSQL
RLS session helpers (`SetLocalAsync`) closed the remaining two criteria.

## Supported database engines (6, all live-tested)

| Dialect (`[assembly: InquiryDialect("…")]`) | Runtime package | Analyzer (source generator) | Live test status |
|---|---|---|---|
| `Sqlite` | `Ignyte.Inquiry.Sqlite` | `Inquiry.Sqlite.Analyzer` | in-process (no Docker) |
| `SqlServer` | `Ignyte.Inquiry.SqlServer` | `Inquiry.SqlServer.Analyzer` | Testcontainers (CI integration matrix) |
| `PostgreSql` | `Ignyte.Inquiry.PostgreSql` | `Inquiry.PostgreSql.Analyzer` | Testcontainers (CI integration matrix) |
| `MySql` | `Ignyte.Inquiry.MySql` | `Inquiry.MySql.Analyzer` | Testcontainers (CI integration matrix) |
| `MariaDb` | `Ignyte.Inquiry.MariaDb` | `Inquiry.MariaDb.Analyzer` | Testcontainers (CI integration matrix) |
| `Oracle` | `Ignyte.Inquiry.Oracle` | `Inquiry.Oracle.Analyzer` | Testcontainers (CI integration matrix) |

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

Packages are versioned by [MinVer](https://github.com/adamralph/minver) from git tags. Preview releases
(`v1.0.0-preview.N`) are published on nuget.org; the first stable release will use the `v1.0.0` tag and
package version `1.0.0`. Every package embeds
SourceLink metadata (`Microsoft.SourceLink.GitHub`) and ships a `.snupkg` symbol package, including the
provider analyzer PDBs. The verifier binds each PDB to its DLL CodeView identity and checks complete
SourceLink document coverage at the exact commit. The previous
tag-triggered rebuild-and-wildcard-push workflow was removed because it did not prove the complete provider
or package-consumer gates and could not safely recover from partial publication. `eng/release-manifest.json`
now defines the exact ten-package 1.0 bundle, and the cross-platform verifier rejects inventory, version,
dependency, repository-commit, metadata, symbol, and SourceLink drift. `eng/pack-release.ps1` packs from
an exact commit in a detached worktree, and CI separates the package producer from an independent verifier
before the versioned `ci-required-v1` aggregate gate can pass. Publishing is live: release tags publish
to nuget.org via Trusted Publishing (see [Contributing — Releasing](contributing.md#releasing)).
[#87](https://github.com/IgnyteSoftware/inquiry/issues/87) (benchmark truth and regression gates) is closed:
corrected baselines committed, weekly regression budgets enforced, and EF Core coverage gaps closed.
[#89](https://github.com/IgnyteSoftware/inquiry/issues/89) closed 2026-07-25 across all nine acceptance
criteria: package identity, an immutable CI artifact, clean consumer installs on net8/net9/net10 and
NativeAOT from the produced nupkgs, an API-compatibility baseline, CycloneDX SBOM plus SLSA build
provenance and dependency auditing, release/support/security policies, hosted versioned documentation,
CODEOWNERS and branch protection, and CodeQL plus dependency scanning. See
[Contributing — Releasing](contributing.md#releasing).

## Security status

The early repository scan findings fixed in `318ee5f` remain covered. A fresh [#220](https://github.com/IgnyteSoftware/inquiry/pull/220) security diff scan
and threat-model review then found a custom-shell CI bypass; [#220](https://github.com/IgnyteSoftware/inquiry/pull/220) fixed it and added regression coverage.
The post-fix review reported no remaining reportable findings. Security evidence, policies, and release
governance shipped with [#89](https://github.com/IgnyteSoftware/inquiry/issues/89) (closed 2026-07-25):
SECURITY.md, a CycloneDX SBOM and SLSA build provenance on every packed artifact, NuGetAudit over direct
and transitive dependencies, and a CodeQL + dependency-scanning workflow. CodeQL is active now that the
repository is public.

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
(e.g. `dotnet test tests/Inquiry.MySql.Tests -f net8.0`). The provider-restoration gate in
[#171](https://github.com/IgnyteSoftware/inquiry/issues/171) is closed. Two consecutive full CI runs
are green at 20/20 required checks each, including every one of the 15 PostgreSQL, MySQL, MariaDB,
SQL Server, and Oracle × net8.0/net9.0/net10.0 integration legs. Docker-gated suites skip locally
(not in CI) when Docker is unavailable.

The normal CI workflow runs on pull requests targeting `main`, and on merge-queue events:
**build-and-unit** (generator, runtime, and SQLite suites — no Docker), **aot-smoke** (publishes and
runs the NativeAOT smoke app), an **integration** matrix (PostgreSQL, MySQL, MariaDB, SQL Server,
Oracle × net8.0/net9.0/net10.0 via Testcontainers — exactly 15 required legs), **package-producer**,
and the independent **package-verifier**. The `ci-required-v1` aggregator runs even after failures and
fails unless all required jobs and matrix legs succeed. Direct merging has been retired; repository
rulesets protect `main` with this context and the review requirements documented under
[#89](https://github.com/IgnyteSoftware/inquiry/issues/89). A separate **scheduled weekly workflow**
(`scheduled.yml`) repeats the full provider × net8.0/net9.0/net10.0 matrix every Monday.
