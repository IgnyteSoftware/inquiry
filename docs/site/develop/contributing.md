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

## Merge to `main` directly — no pull requests

Merge a feature branch into `main` once it's complete, reviewed, and green. The project does not use PRs.

## Commit messages

Commit messages are written to a BOM-free file and committed via `git commit -F <file>`, ending with the
`Co-Authored-By: Claude …` trailer. (PowerShell here-strings bind unreliably to native `git`; the file
approach is the convention.)

## CI

[`.github/workflows/ci.yml`](https://github.com/JakeOverstreet/inquiry/blob/main/.github/workflows/ci.yml)
runs on pushes to `main` and also on the `pull_request` event if a PR is opened:

- a **build-and-unit** job — the generator, runtime, and SQLite suites (no Docker);
- an **aot-smoke** job — publishes the `Inquiry.AotSmoke` sample as a native binary and executes it,
  verifying the NativeAOT story end-to-end; and
- an **integration** job — a matrix of **PostgreSQL, MySQL, SQL Server, and Oracle** live suites, each on
  **net8.0** and **net9.0**, provisioned with Testcontainers.

CI uploads TRX result artifacts (`if: always()`) so failures and skips can be inspected.

A separate **scheduled weekly workflow**
([`scheduled.yml`](https://github.com/JakeOverstreet/inquiry/blob/main/.github/workflows/scheduled.yml))
runs every Monday and extends the integration matrix to **net10.0**, re-verifying every provider against
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

1. Ensure `main` is green — CI must pass.
2. Tag the release commit: `git tag v1.0.0`
3. Push the tag: `git push --tags`

The [`release.yml`](https://github.com/JakeOverstreet/inquiry/blob/main/.github/workflows/release.yml)
workflow triggers on any `v*` tag push and:

- Checks out with full history (MinVer needs tags to derive the version).
- Builds in Release configuration.
- Runs generator, runtime, and SQLite tests as a gate.
- Packs all 9 shippable packages (+ `.snupkg` symbol packages).
- Pushes to NuGet.org using the `NUGET_API_KEY` repository secret.

### Prerequisites

A `NUGET_API_KEY` secret must be configured in the repository's GitHub Actions secrets
(Settings > Secrets and variables > Actions). Generate an API key at
[nuget.org/account/apikeys](https://www.nuget.org/account/apikeys) scoped to the Inquiry packages.

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
`Microsoft.SourceLink.GitHub`, and ships a `.snupkg` symbol package. NuGet consumers can step into
Inquiry source in their debugger without downloading the repo.

## Adding a database

See [Adding a provider](adding-a-provider.md) for the append-point checklist.
