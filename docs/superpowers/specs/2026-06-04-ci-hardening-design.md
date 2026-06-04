# Design — CI Hardening: skip-gate + scheduled full-TFM matrix

- **Date:** 2026-06-04
- **Status:** Approved (scope + approach confirmed via scoping questions — see §2).
- **Owner:** in-session work, project process (brainstorm → spec → plan → execute).

## 1. Goal

Two CI improvements from the [Roadmap](../../site/develop/roadmap.md):

1. **Skip-gate.** A provider integration suite that silently skips — e.g. Docker fails to start on a
   runner, so every `SkippableFact` skips — must **fail CI** instead of staying green and masking that the
   live tests never ran.
2. **net10.0 provider coverage.** Provider integration currently runs only on net8.0/net9.0. Add the
   missing net10.0 coverage via a **scheduled weekly** full provider × TFM matrix, keeping PR CI fast.

Deferred (out of scope): a repo-wide warning-count gate.

## 2. Scope decisions (from scoping questions)

- **Scope:** skip-gate + scheduled net10 matrix. Warning gate deferred.
- **Skip-gate approach:** a **require-Docker guard that fails at the source** (env-gated), not a TRX-parsing
  CI step. Cleaner failure, no maintained skip baseline, and intentional skips (SQL Server FTS) are left
  alone because they occur with the container *available*.
- **net10 matrix:** a **separate scheduled weekly workflow**, not added to the PR matrix.

## 3. Current state

- **`.github/workflows/ci.yml`** — two jobs: `build-and-unit` (generator/unit/SQLite; SDK 8/9/10; TRX
  uploaded) and `integration` (matrix `provider: [PostgreSql, MySql, SqlServer, Oracle] × tfm:
  [net8.0, net9.0]`; TRX uploaded; 40-min timeout). No scheduled/nightly workflow.
- **4 container fixtures** (`PostgreSqlContainerFixture`, `MySqlContainerFixture`,
  `SqlServerContainerFixture`, `OracleContainerFixture`) each `try/catch` the container start; on failure
  they set `IsAvailable = false` + `SkipReason`, and `SkippableFact` tests skip. SQLite is in-process
  (no fixture, always runs).
- **`tests/Inquiry.IntegrationTesting`** — shared test-support library referenced by every provider test
  project. The natural home for a shared guard helper.

## 4. Design

### 4.1 Require-Docker guard (skip-gate)

New static helper `DockerRequirement` in `Inquiry.IntegrationTesting`:

```csharp
public static class DockerRequirement
{
    public const string EnvVarName = "INQUIRY_REQUIRE_DOCKER";

    public static bool IsRequired() => Environment.GetEnvironmentVariable(EnvVarName) == "1";

    // Called by each container fixture at the end of InitializeAsync.
    public static void ThrowIfRequiredButUnavailable(bool isAvailable, string? skipReason)
        => Enforce(IsRequired(), isAvailable, skipReason);

    // Pure core — unit-testable without mutating environment variables.
    public static void Enforce(bool isRequired, bool isAvailable, string? skipReason)
    {
        if (isRequired && !isAvailable)
        {
            throw new InvalidOperationException(
                $"{EnvVarName}=1 requires a running test container, but it did not start: {skipReason}");
        }
    }
}
```

Each of the 4 container fixtures calls `DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable,
SkipReason)` at the end of `InitializeAsync` (after the `try/catch` has set `IsAvailable`). Behavior:

| Docker | `INQUIRY_REQUIRE_DOCKER` | Result |
|---|---|---|
| up | (any) | `IsAvailable == true` → no throw → normal run |
| down | unset (local dev) | no throw → tests **skip** (unchanged) |
| down | `1` (CI) | throw → collection-fixture init fails → suite **fails loudly** with the reason |

`ci.yml` integration job gains `env: INQUIRY_REQUIRE_DOCKER: "1"`. Intentional skips (SQL Server FTS) are
untouched — they occur with `IsAvailable == true`, so the guard never fires on them.

### 4.2 Scheduled weekly full matrix

New `.github/workflows/scheduled.yml`:

- **Triggers:** `schedule` with cron `0 6 * * 1` (Mondays 06:00 UTC) + `workflow_dispatch` (manual/on-demand).
- **One job** mirroring `ci.yml`'s `integration` job but with `tfm: [net8.0, net9.0, net10.0]` (4×3 = 12
  legs), `fail-fast: false`, 40-min timeout, `setup-dotnet` 8/9/10, `env: INQUIRY_REQUIRE_DOCKER: "1"`,
  per-leg TRX uploaded as artifacts.

This adds the missing net10.0 coverage and serves as a periodic full re-verification (e.g. against updated
container images), while PR CI stays on net8.0/net9.0.

### 4.3 Out of scope

- Repo-wide warning-count gate (deferred to the Roadmap).
- PR integration matrix unchanged (net8.0/net9.0).
- `build-and-unit` job unchanged (no Docker there, so no guard/env needed).

## 5. Testing & verification

- **Unit test** for `DockerRequirement.Enforce` covering the four `(isRequired × isAvailable)` cases —
  pure, deterministic, no environment mutation. Lives in a non-Docker test project that references
  `Inquiry.IntegrationTesting` (e.g. `Inquiry.Sqlite.Tests`).
- **Local path check (if Docker is absent in the dev environment):** run a provider suite with
  `INQUIRY_REQUIRE_DOCKER` unset → tests **skip**; rerun with `INQUIRY_REQUIRE_DOCKER=1` → suite **fails**
  with the guard message. Exercises both real paths.
- **CI-run validation is partial by nature:** YAML is reviewed locally; the scheduled workflow can be
  triggered once via `workflow_dispatch` to confirm green. The skip-gate's CI behavior is fully proven
  only on the next real CI run (it is a no-op unless a container fails to start).

## 6. Success criteria

1. `DockerRequirement` helper exists with a unit test covering all four cases (green).
2. All 4 container fixtures invoke the guard at the end of `InitializeAsync`.
3. `ci.yml` integration job sets `INQUIRY_REQUIRE_DOCKER=1`.
4. `.github/workflows/scheduled.yml` runs the 4×3 provider×TFM matrix weekly + on demand, Docker-required,
   with TRX artifacts.
5. Local behavior is unchanged when the env var is unset (suites still skip without Docker).
6. Non-Docker suites and intentional FTS skips are unaffected.
7. Existing non-Docker suites stay green (generator 178, SQLite e2e + the new helper test).
