# Contributing to Inquiry

Thanks for your interest in improving Inquiry! This page covers the practical workflow for
getting a change from idea to published package. The deeper conventions (worktrees, TDD
style, generator "hot spine" rules) live in the
[contributor docs](docs/site/develop/contributing.md).

## TL;DR workflow

```
feature branch ──PR──▶ prerelease ──promotion PR──▶ main
                          │                           │
                          ▼                           ▼
              Ignyte.Inquiry.* X.Y.Z-preview.N   Ignyte.Inquiry.* X.Y.Z
                  on nuget.org                     on nuget.org + tag vX.Y.Z
```

1. **Open an issue first** for anything non-trivial so the approach can be agreed before you
   invest time.
2. **Branch from `prerelease`** (`feature/<short-name>` or `fix/<short-name>`).
3. **Write the failing test first**, then the fix/feature. Generator changes start with an
   emission test asserting the exact generated SQL/`const string`.
4. **Open a PR into `prerelease`.** CI must pass: build + unit + SQLite suites, NativeAOT
   smoke, the 15-leg live-database integration matrix (5 providers × 3 TFMs), benchmark
   smoke, and the package contract producer/verifier. At least one human review is required.
5. When `prerelease` is ready to ship, a **promotion PR** from `prerelease` into `main` is
   opened, reviewed, and merged. Nothing merges to `main` directly.

## What merging does (releases)

Publishing is automated by [`release.yml`](.github/workflows/release.yml) and driven by
[`eng/release-manifest.json`](eng/release-manifest.json), whose `packageVersion` is the
single source of truth:

- **Merge into `prerelease`** → every package is packed, verified, and published to
  nuget.org as `<packageVersion>-preview.<run-number>` (e.g. `1.0.0-preview.42`).
- **Merge into `main`** → the exact `packageVersion` is packed, verified, published to
  nuget.org as a stable release, tagged `v<packageVersion>`, and a GitHub release is
  created with the packages and SBOM attached.
- If the manifest version has already been tagged, the `main` run is a no-op — so a
  release requires a PR that **bumps `packageVersion`** (and the manifest's `tag` and
  inter-package dependency versions to match).

Do not push release tags manually; the workflow creates them.

## Packages

Everything ships under the `Ignyte.` prefix (the bare `Inquiry` ID was already taken on
nuget.org); assemblies and namespaces remain `Inquiry.*`:

| Package | Contents |
|---|---|
| `Ignyte.Inquiry` | Core runtime — attributes, pipeline, DI |
| `Ignyte.Inquiry.Sqlite` / `.SqlServer` / `.PostgreSql` / `.MySql` / `.MariaDb` / `.Oracle` | Provider runtime + bundled source-generator analyzer |
| `Ignyte.Inquiry.AspNetCore` | Audit-context middleware (`UseInquiryAuditContext`) |
| `Ignyte.Inquiry.Interceptors` | Slow-query logging, sqlcommenter, N+1 detection |
| `Ignyte.Inquiry.Testing` | SQLite fixture, recording interceptor, Respawn reset |

## Local development

```powershell
dotnet build          # first build wires up the analyzers; IDE squiggles vanish after it
dotnet test           # database suites skip gracefully when Docker is absent
```

Live provider tests need only Docker (via Testcontainers) — no local database installs.
See [contributor docs](docs/site/develop/contributing.md) for the SQL Server full-text
image and the rest of the conventions, and
[Adding a provider](docs/site/develop/adding-a-provider.md) if you're adding a database.

## Ground rules

- Warnings are errors everywhere; keep the build clean.
- Public API changes must update the `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`
  files (enforced by Microsoft.CodeAnalysis.PublicApiAnalyzers).
- New packable projects must be added to `eng/release-manifest.json` — the package
  contract verifier fails CI if the manifest and the packable project inventory drift.
- Keep PRs to one concern; refactors, features, and fixes travel separately.
