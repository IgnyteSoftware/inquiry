# Contributing

How Inquiry is developed. A few hard conventions keep the parallel, source-generator-heavy work merge-clean
and verifiable.

## Skill-first workflow

Start work through the relevant workflow skill: brainstorm a new feature into a spec, turn the spec into an
implementation plan, then execute it task-by-task. Use the debugging workflow for bugs. Specs and plans are
kept in-repo under `docs/superpowers/`.

## Worktrees + parallel agents

Large, separable workstreams are built in isolated git worktrees by parallel agents, then merged one at a
time. The "hot spine" of shared generator files (the `SqlBuilder` hierarchy, `StoreProcessor`,
`StoreOperationEmitter`, `EntityProcessor`, the diagnostic registry) is edited via a serialized
**foundation** pass first, so parallel branches don't collide — see [Design notes](design-notes.md). For
small, short-lived tasks, work in-session rather than spawning agents.

## TDD

Red generator-emission test first (assert the exact emitted `const string`) → implement → integration test.
SQLite integration tests always run in-process; the other dialects run against a real engine via
Testcontainers.

## Live testing needs only Docker

No database engine is installed on the host. Each provider test project link-compiles the shared Northwind
source under **its own** dialect, so it exercises that engine's real SQL. Containers come from
Testcontainers; tests **skip gracefully** (they do not fail) when Docker is absent, so `dotnet test` stays
green on a machine without Docker.

SQL Server Full-Text Search is optional in Microsoft's base image. To run the release-required SQL Server
suite locally, build the repository's pinned CU14 image and select it explicitly:

```powershell
docker build --file tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerFts.Dockerfile --tag inquiry-sqlserver-fts:2022-cu14 .
$env:INQUIRY_SQLSERVER_IMAGE = "inquiry-sqlserver-fts:2022-cu14"
$env:INQUIRY_REQUIRE_DOCKER = "1"
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net10.0
```

Required runs fail if the selected image cannot start, runs as root, or reports that Full-Text Search is
unavailable. With `INQUIRY_REQUIRE_DOCKER` unset and no image override, ordinary local runs keep using the
official base image and skip only the full-text tests when that optional component is absent.

## First build after clone: expect IDE squiggles

The analyzers are attached to consuming projects via built DLL paths in `Directory.Build.targets`
(guarded by `Exists()`), so after a fresh clone or `git clean` the IDE cannot load the generator and
every `partial` store method shows CS8795 until you run `dotnet build` once. This only affects
working in this repository — NuGet consumers get the analyzer from the package and never see it.
See the [getting-started troubleshooting section](../articles/getting-started.md#troubleshooting-red-squiggles-under-partial-methods)
for the consumer-facing checklist.

## Code review before merge

Run the code-review workflow on a feature branch before merging; fix Critical/Important findings first.

## Pull requests and integration branches

All changes land through reviewed pull requests. During 1.0 stabilization, feature branches target
`prerelease`; promotion to `main` is a separate reviewed pull request after the release gates pass. Direct
pushes, force-pushes, branch deletion, and merge commits are not part of the supported workflow.

The external rulesets for both `prerelease` and `main` must require linear history, an up-to-date branch,
resolved conversations, at least one real human approval, and the final `ci-required-v1` status. Copilot and
other automated reviews supplement, but do not replace, the human approval. These are repository-setting
requirements: the checked-in workflow cannot enforce them by itself.

## Commit messages

Commit messages are written to a BOM-free file and committed via `git commit -F <file>`, ending with the
`Co-Authored-By: Claude …` trailer. (PowerShell here-strings bind unreliably to native `git`; the file
approach is the convention.)

## CI

[`.github/workflows/ci.yml`](https://github.com/JakeOverstreet/inquiry/blob/main/.github/workflows/ci.yml)
runs for pull requests into `prerelease` and `main`, and for merge-queue `merge_group` events:

- a **build-and-unit** job — the generator, runtime, and SQLite suites (no Docker);
- an **aot-smoke** job — publishes the `Inquiry.AotSmoke` sample as a native binary and executes it,
  verifying the NativeAOT story end-to-end; and
- an **integration** job — a matrix of **PostgreSQL, MySQL, MariaDB, SQL Server, and Oracle** live suites, each on
  **net8.0**, **net9.0**, and **net10.0** (exactly 15 required legs), provisioned with Testcontainers.

The always-running **ci-required-v1** job fails unless every required job and every matrix leg succeeds.
Its versioned source of truth is `eng/ci-required-v1.json`; contract tests prevent the workflow matrix or
aggregator from drifting away from it. CI uploads TRX result artifacts even after failures, fails if the
evidence is absent, and retains the artifacts for 14 days.

A separate **scheduled weekly workflow**
([`scheduled.yml`](https://github.com/JakeOverstreet/inquiry/blob/main/.github/workflows/scheduled.yml))
runs every Monday and repeats the full three-TFM integration matrix, re-verifying every provider against
current container images.

**Warnings are errors everywhere.** Production projects set `TreatWarningsAsErrors`, and
`tests/Directory.Build.props` extends the same gate to every test project, so a new compiler/analyzer
warning in test code fails the build instead of slipping through. The only intentionally-warning
projects are the DLG comparison benchmarks under `benchmarks/`, which are out of the gate.

## Releasing

Inquiry uses **MinVer** for tag-based versioning. The version is derived from the nearest git tag:

| State | Example version |
|---|---|
| Tagged commit `v1.0.0` | `1.0.0` |
| Tagged commit `v1.0.0-preview.1` | `1.0.0-preview.1` |
| 3 commits after `v1.0.0` | `1.0.1-alpha.0.3` |
| No tag (floor is 1.0) | `1.0.0-alpha.0.N` |

### How to release

Public publishing is disabled while the immutable release-candidate pipeline is being completed under
[#89](https://github.com/JakeOverstreet/inquiry/issues/89). Do not create or push a release tag manually.
The retired tag workflow rebuilt source and wildcard-pushed packages; that path was intentionally removed.

For local package-contract verification only, pack the nine manifest entries at an exact commit:

```powershell
$output = Join-Path ([System.IO.Path]::GetTempPath()) 'inquiry-release-candidate'
./eng/pack-release.ps1 -OutputPath $output -Commit (git rev-parse HEAD)
```

This requires a clean worktree at the named `HEAD`, creates a detached temporary worktree at that exact
commit, and restores and builds only from that immutable snapshot. It uses `MinVerVersionOverride=1.0.0`,
refuses a non-empty or linked output directory, and validates exact nupkg and snupkg inventory, versions,
dependencies, repository commit, metadata, assets, DLL/PDB identities, and complete SourceLink mappings.
The canonical repository branch is recorded as `refs/heads/prerelease`, even though compilation occurs from
the detached commit snapshot.
It does not publish or create a tag. The protected promotion and resumable publication stages are not
implemented yet; they must consume the verified bundle without rebuilding it.

### Release prerequisites

Repository visibility/plan, branch and tag rulesets, Pages, security features, the protected release
environment, NuGet owners and package IDs, and trusted-publishing policy must pass the fail-closed settings
attestation before RC. Repository code does not change those external settings. A short-lived scoped NuGet
key is fallback-only and requires explicit owner approval; trusted OIDC publishing is the intended path.

### Shippable packages

| Package | Description |
|---|---|
| `Inquiry` | Core runtime — attributes, pipeline, DI |
| `Inquiry.SqlServer` | SQL Server provider + bundled analyzer |
| `Inquiry.PostgreSql` | PostgreSQL provider + bundled analyzer |
| `Inquiry.MySql` | MySQL provider + bundled analyzer |
| `Inquiry.MariaDb` | MariaDB provider + bundled analyzer |
| `Inquiry.Oracle` | Oracle provider + bundled analyzer |
| `Inquiry.Sqlite` | SQLite provider + bundled analyzer |
| `Inquiry.Interceptors` | Opt-in slow-query logging + sqlcommenter |
| `Inquiry.Testing` | SQLite fixture, recording interceptor, Respawn reset |

Benchmark, sample, test, and analyzer projects are marked `IsPackable=false` and excluded from
`dotnet pack`.

### SourceLink and symbol packages

Every package embeds SourceLink metadata (commit hash, repository URL) via
`Microsoft.SourceLink.GitHub`, and ships a `.snupkg` symbol package. Provider symbol packages include
portable PDBs for both bundled analyzer DLLs as well as the runtime assemblies. The verifier binds every
PDB to its DLL CodeView identity and requires every source document to resolve to the named commit.
NuGet consumers can step into Inquiry source in their debugger without downloading the repo.

## Adding a database

See [Adding a provider](adding-a-provider.md) for the append-point checklist.
