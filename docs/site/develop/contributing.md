# Contributing

How Inquiry is developed. A few hard conventions keep the parallel, source-generator-heavy work merge-clean
and verifiable.

## Skill-first workflow

Start work through the relevant workflow skill: brainstorm a new feature into a spec, turn the spec into an
implementation plan, then execute it task-by-task. Use the debugging workflow for bugs.

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

Repository rulesets (applied by [`eng/configure-branch-protection.ps1`](https://github.com/IgnyteSoftware/inquiry/blob/main/eng/configure-branch-protection.ps1))
protect both `prerelease` and `main`: linear history, an up-to-date branch, resolved conversations, one
human approval with code-owner review and last-push approval, and the final `ci-required-v1` status pinned
to GitHub Actions. A second ruleset blocks update and deletion of `v*` release tags for everyone (creation stays
open so the release workflow can tag; GitHub rejects the Actions app as a bypass actor). Organization admins are a named, audited bypass actor (a solo-maintainer
necessity — remove that bypass once a second reviewer exists). Copilot and other automated reviews
supplement, but do not replace, the human approval. These are repository-setting requirements: the
checked-in workflow cannot enforce them by itself.

## Commit messages

Commit messages are written to a BOM-free file and committed via `git commit -F <file>`, ending with the
`Co-Authored-By: Claude …` trailer. (PowerShell here-strings bind unreliably to native `git`; the file
approach is the convention.)

## CI

[`.github/workflows/ci.yml`](https://github.com/IgnyteSoftware/inquiry/blob/main/.github/workflows/ci.yml)
runs for pull requests into `prerelease` and `main`, and for merge-queue `merge_group` events:

- a **build-and-unit** job — the generator, runtime, and SQLite suites (no Docker);
- an **aot-smoke** job — publishes the `Inquiry.AotSmoke` sample as a native binary and executes it,
  verifying the NativeAOT story end-to-end; and
- an **integration** job — a matrix of **PostgreSQL, MySQL, MariaDB, SQL Server, and Oracle** live suites, each on
  **net8.0**, **net9.0**, and **net10.0** (exactly 15 required legs), provisioned with Testcontainers.

The always-running **ci-required-v1** job fails unless every required job and every matrix leg succeeds.
Its versioned source of truth is `eng/ci-required-v1.json`; contract tests prevent the workflow matrix or
aggregator from drifting away from it. CI uploads TRX result artifacts even after failures, fails if the
evidence is absent, and retains the artifacts for 7 days.

A separate **scheduled weekly workflow**
([`scheduled.yml`](https://github.com/IgnyteSoftware/inquiry/blob/main/.github/workflows/scheduled.yml))
runs every Monday and repeats the full three-TFM integration matrix, re-verifying every provider against
current container images.

**Warnings are errors everywhere.** Production projects set `TreatWarningsAsErrors`, and
`tests/Directory.Build.props` extends the same gate to every test project, so a new compiler/analyzer
warning in test code fails the build instead of slipping through. The only intentionally-warning
projects are the DLG comparison benchmarks under `benchmarks/`, which are out of the gate.

## Releasing

Releases are automated by
[`release.yml`](https://github.com/IgnyteSoftware/inquiry/blob/main/.github/workflows/release.yml) and
driven by `eng/release-manifest.json`, whose `packageVersion` is the single source of truth:

| Event | Published version |
|---|---|
| PR merged into `prerelease` | `<packageVersion>-preview.<run-number>` on nuget.org |
| PR merged into `main` (manifest bumped) | `<packageVersion>` on nuget.org + git tag `v<packageVersion>` + GitHub release |
| PR merged into `main` (version already tagged) | No publish — verified no-op |

To cut a release, open a PR that bumps `packageVersion` (and the matching `tag` and inter-package
dependency versions) in `eng/release-manifest.json`; the release ships when that PR reaches `main`.
Do not create or push release tags manually — the workflow creates them after a successful publish.
Authentication uses NuGet **Trusted Publishing** (OIDC via `NuGet/login`); no long-lived API key is
stored in the repository.

Every release run packs from an immutable detached snapshot of the exact merge commit and re-verifies
the full package contract before pushing. For local package-contract verification, pack the ten
manifest entries at an exact commit:

```powershell
$output = Join-Path ([System.IO.Path]::GetTempPath()) 'inquiry-release-candidate'
./eng/pack-release.ps1 -OutputPath $output -Commit (git rev-parse HEAD)
# preview variant:
./eng/pack-release.ps1 -OutputPath $output -Commit (git rev-parse HEAD) `
    -PackageVersion "1.0.0-preview.1" -RepositoryBranch "refs/heads/prerelease"
```

This requires a clean worktree at the named `HEAD`, creates a detached temporary worktree at that exact
commit, and restores and builds only from that immutable snapshot. It refuses a non-empty or linked
output directory, and validates exact nupkg and snupkg inventory, versions, dependencies, repository
commit, metadata, assets, DLL/PDB identities, and complete SourceLink mappings. It does not publish or
create a tag.

### Shippable packages

Packages ship under the `Ignyte.` prefix (the bare `Inquiry` ID is taken on nuget.org); assemblies and
namespaces remain `Inquiry.*`:

| Package | Description |
|---|---|
| `Ignyte.Inquiry` | Core runtime — attributes, pipeline, DI |
| `Ignyte.Inquiry.SqlServer` | SQL Server provider + bundled analyzer |
| `Ignyte.Inquiry.PostgreSql` | PostgreSQL provider + bundled analyzer |
| `Ignyte.Inquiry.MySql` | MySQL provider + bundled analyzer |
| `Ignyte.Inquiry.MariaDb` | MariaDB provider + bundled analyzer |
| `Ignyte.Inquiry.Oracle` | Oracle provider + bundled analyzer |
| `Ignyte.Inquiry.Sqlite` | SQLite provider + bundled analyzer |
| `Ignyte.Inquiry.AspNetCore` | ASP.NET Core audit-context middleware |
| `Ignyte.Inquiry.Interceptors` | Opt-in slow-query logging + sqlcommenter |
| `Ignyte.Inquiry.Testing` | SQLite fixture, recording interceptor, Respawn reset |

Benchmark, sample, test, and analyzer projects are marked `IsPackable=false` and excluded from
`dotnet pack`. A new packable project must be added to `eng/release-manifest.json`; the package
contract verifier fails CI when the manifest and packable-project inventory drift.

### SourceLink and symbol packages

Every package embeds SourceLink metadata (commit hash, repository URL) via
`Microsoft.SourceLink.GitHub`, and ships a `.snupkg` symbol package. Provider symbol packages include
portable PDBs for both bundled analyzer DLLs as well as the runtime assemblies. The verifier binds every
PDB to its DLL CodeView identity and requires every source document to resolve to the named commit.
NuGet consumers can step into Inquiry source in their debugger without downloading the repo.

## Adding a database

See [Adding a provider](adding-a-provider.md) for the append-point checklist.
