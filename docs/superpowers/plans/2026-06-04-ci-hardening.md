# CI Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a silently-skipped provider suite fail CI (a require-Docker guard), and add the missing net10.0 provider coverage via a scheduled weekly full-TFM matrix.

**Architecture:** A new `DockerRequirement` helper in the shared `Inquiry.IntegrationTesting` library is called by each of the 4 container fixtures at the end of `InitializeAsync`; when `INQUIRY_REQUIRE_DOCKER=1` (set only on CI) and the container didn't start, it throws — turning a silent skip into a loud failure. A new `scheduled.yml` workflow runs the provider × net8/9/10 matrix weekly.

**Tech Stack:** .NET (netstandard2.0 helper, xUnit), GitHub Actions (YAML), Testcontainers.

**Branch:** `ci/hardening` (already created; spec committed as `234dc3b`).

**Ground truth (verified against current source):**
- `Inquiry.IntegrationTesting` — netstandard2.0, `Nullable enable`, namespace `Inquiry.IntegrationTesting`. Referenced by all provider test projects and by `Inquiry.Sqlite.Tests`.
- 4 container fixtures, identical shape (`bool IsAvailable`, `string? SkipReason`, `async Task InitializeAsync` with `try` start / `catch` → `IsAvailable=false; SkipReason="<Provider> container unavailable (is Docker running?): " + ex.Message;`):
  - `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlContainerFixture.cs`
  - `tests/Inquiry.MySql.Tests/Fixtures/MySqlContainerFixture.cs`
  - `tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerContainerFixture.cs`
  - `tests/Inquiry.Oracle.Tests/Fixtures/OracleContainerFixture.cs`
- `.github/workflows/ci.yml` — `integration` job matrix `provider: [PostgreSql, MySql, SqlServer, Oracle] × tfm: [net8.0, net9.0]`, 40-min timeout, TRX uploaded.

---

## File Structure

**Create:**
- `tests/Inquiry.IntegrationTesting/DockerRequirement.cs` — the env-gated guard helper.
- `tests/Inquiry.Sqlite.Tests/DockerRequirementTests.cs` — unit test for the pure `Enforce` core.
- `.github/workflows/scheduled.yml` — weekly full provider × TFM matrix.

**Modify:**
- The 4 container fixtures — add `using Inquiry.IntegrationTesting;` + the guard call.
- `.github/workflows/ci.yml` — add `INQUIRY_REQUIRE_DOCKER: "1"` to the `integration` job.

---

## Task 1: `DockerRequirement` helper + unit test (TDD)

**Files:**
- Create: `tests/Inquiry.Sqlite.Tests/DockerRequirementTests.cs`
- Create: `tests/Inquiry.IntegrationTesting/DockerRequirement.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Inquiry.Sqlite.Tests/DockerRequirementTests.cs`:

```csharp
using System;
using Inquiry.IntegrationTesting;
using Xunit;

namespace Inquiry.Sqlite.Tests;

public class DockerRequirementTests
{
    [Fact]
    public void EnforceThrowsWhenRequiredAndUnavailable()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DockerRequirement.Enforce(isRequired: true, isAvailable: false, skipReason: "Docker down"));
        Assert.Contains(DockerRequirement.EnvVarName, ex.Message);
        Assert.Contains("Docker down", ex.Message);
    }

    [Fact]
    public void EnforceDoesNotThrowWhenRequiredAndAvailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: true, isAvailable: true, skipReason: null)));

    [Fact]
    public void EnforceDoesNotThrowWhenNotRequiredAndUnavailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: false, isAvailable: false, skipReason: "Docker down")));

    [Fact]
    public void EnforceDoesNotThrowWhenNotRequiredAndAvailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: false, isAvailable: true, skipReason: null)));
}
```

- [ ] **Step 2: Run the test to verify it fails (compile error: type not found)**

Run: `dotnet test tests/Inquiry.Sqlite.Tests -f net8.0 --filter "FullyQualifiedName~DockerRequirementTests" --nologo`
Expected: BUILD FAILS — `error CS0103: The name 'DockerRequirement' does not exist` (the helper doesn't exist yet).

- [ ] **Step 3: Create the helper**

Create `tests/Inquiry.IntegrationTesting/DockerRequirement.cs`:

```csharp
using System;

namespace Inquiry.IntegrationTesting;

/// <summary>
/// Gates Docker-backed integration suites in CI. When <c>INQUIRY_REQUIRE_DOCKER=1</c> is set (CI only)
/// and a test container did not start, the run must FAIL rather than silently skip — otherwise a runner
/// with broken Docker leaves CI green while the live tests never ran. Locally (env var unset) the suites
/// still skip when Docker is absent.
/// </summary>
public static class DockerRequirement
{
    /// <summary>Environment variable that, when set to <c>"1"</c>, makes a missing container a hard failure.</summary>
    public const string EnvVarName = "INQUIRY_REQUIRE_DOCKER";

    /// <summary>True when <see cref="EnvVarName"/> is set to <c>"1"</c> (set on CI, unset locally).</summary>
    public static bool IsRequired() => Environment.GetEnvironmentVariable(EnvVarName) == "1";

    /// <summary>
    /// Called by each container fixture at the end of <c>InitializeAsync</c>. Throws when Docker is
    /// required (CI) but the container did not start; otherwise a no-op.
    /// </summary>
    public static void ThrowIfRequiredButUnavailable(bool isAvailable, string? skipReason)
        => Enforce(IsRequired(), isAvailable, skipReason);

    /// <summary>Pure core, separated from the environment read so it is deterministically unit-testable.</summary>
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

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Inquiry.Sqlite.Tests -f net8.0 --filter "FullyQualifiedName~DockerRequirementTests" --nologo`
Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5: Commit**

```bash
git add tests/Inquiry.IntegrationTesting/DockerRequirement.cs tests/Inquiry.Sqlite.Tests/DockerRequirementTests.cs
git commit -F <bom-free-msg-file>
```
Message: `test(ci): add DockerRequirement guard helper + unit tests`

---

## Task 2: Wire the guard into the 4 container fixtures

**Files (modify):**
- `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlContainerFixture.cs`
- `tests/Inquiry.MySql.Tests/Fixtures/MySqlContainerFixture.cs`
- `tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerContainerFixture.cs`
- `tests/Inquiry.Oracle.Tests/Fixtures/OracleContainerFixture.cs`

Each fixture needs two edits: add the `using`, and add the guard call after the `catch`. The `SkipReason` string is unique per provider, which anchors the second edit.

- [ ] **Step 1: PostgreSql — add using**

In `PostgreSqlContainerFixture.cs`, replace:
```csharp
using System.Threading.Tasks;
```
with:
```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
```

- [ ] **Step 2: PostgreSql — add guard after the catch**

Replace:
```csharp
            IsAvailable = false;
            SkipReason = "PostgreSQL container unavailable (is Docker running?): " + ex.Message;
        }
    }
```
with:
```csharp
            IsAvailable = false;
            SkipReason = "PostgreSQL container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }
```

- [ ] **Step 3: MySql — add using**

In `MySqlContainerFixture.cs`, replace:
```csharp
using System.Threading.Tasks;
```
with:
```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
```

- [ ] **Step 4: MySql — add guard after the catch**

Replace:
```csharp
            IsAvailable = false;
            SkipReason = "MySQL container unavailable (is Docker running?): " + ex.Message;
        }
    }
```
with:
```csharp
            IsAvailable = false;
            SkipReason = "MySQL container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }
```

- [ ] **Step 5: SqlServer — add using**

In `SqlServerContainerFixture.cs`, replace:
```csharp
using System.Threading.Tasks;
```
with:
```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
```

- [ ] **Step 6: SqlServer — add guard after the catch**

Replace:
```csharp
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;
        }
    }
```
with:
```csharp
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }
```

- [ ] **Step 7: Oracle — add using**

In `OracleContainerFixture.cs`, replace:
```csharp
using System.Threading.Tasks;
```
with:
```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
```

- [ ] **Step 8: Oracle — add guard after the catch**

Replace:
```csharp
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;
        }
    }
```
with:
```csharp
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }
```

- [ ] **Step 9: Build the 4 provider test projects (compile check)**

Run: `dotnet build tests/Inquiry.PostgreSql.Tests tests/Inquiry.MySql.Tests tests/Inquiry.SqlServer.Tests tests/Inquiry.Oracle.Tests -c Release`
Expected: build succeeds (the guard call resolves against `Inquiry.IntegrationTesting`).

- [ ] **Step 10: Verify both guard paths locally**

First check Docker: `docker info >/dev/null 2>&1 && echo DOCKER_UP || echo DOCKER_DOWN`

- **If `DOCKER_DOWN`** (no Docker locally) — this directly exercises both guard paths:
  - Skip path (env unset): `dotnet test tests/Inquiry.MySql.Tests -f net8.0 --nologo` → tests **skip** (e.g. `Skipped: N`), exit 0.
  - Fail path (env set): set `INQUIRY_REQUIRE_DOCKER=1` and rerun the same command → the suite **fails** with the guard message `INQUIRY_REQUIRE_DOCKER=1 requires a running test container, but it did not start: MySQL container unavailable …`.
    - bash: `INQUIRY_REQUIRE_DOCKER=1 dotnet test tests/Inquiry.MySql.Tests -f net8.0 --nologo`
    - PowerShell: `$env:INQUIRY_REQUIRE_DOCKER='1'; dotnet test tests/Inquiry.MySql.Tests -f net8.0 --nologo; Remove-Item Env:INQUIRY_REQUIRE_DOCKER`
- **If `DOCKER_UP`** — env-set should still pass (container starts): `INQUIRY_REQUIRE_DOCKER=1 dotnet test tests/Inquiry.MySql.Tests -f net8.0 --nologo` → passes. (The fail path is then covered by the Task 1 unit test only; note this in the final report.)

- [ ] **Step 11: Commit**

```bash
git add tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlContainerFixture.cs tests/Inquiry.MySql.Tests/Fixtures/MySqlContainerFixture.cs tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerContainerFixture.cs tests/Inquiry.Oracle.Tests/Fixtures/OracleContainerFixture.cs
git commit -F <bom-free-msg-file>
```
Message: `test(ci): container fixtures fail (not skip) when INQUIRY_REQUIRE_DOCKER=1`

---

## Task 3: Require Docker in the `ci.yml` integration job

**Files (modify):** `.github/workflows/ci.yml`

- [ ] **Step 1: Add the env var to the integration job**

Replace:
```yaml
    timeout-minutes: 40
    strategy:
```
with:
```yaml
    timeout-minutes: 40
    env:
      # A provider suite that can't start its container must FAIL here, not silently skip and leave CI
      # green (GitHub runners have Docker). See tests/Inquiry.IntegrationTesting/DockerRequirement.cs.
      INQUIRY_REQUIRE_DOCKER: "1"
    strategy:
```

- [ ] **Step 2: Verify YAML parses**

Run (PowerShell, no extra deps): `pwsh -Command "Get-Content .github/workflows/ci.yml -Raw | Out-Null; Write-Host 'read ok'"`
Then eyeball: `env:` is indented 4 spaces (a job-level key, sibling of `runs-on`/`strategy`), and `INQUIRY_REQUIRE_DOCKER` is 6 spaces. (If `yamllint` or `actionlint` is installed, run it: `actionlint .github/workflows/ci.yml`.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -F <bom-free-msg-file>
```
Message: `ci: require Docker in the integration job (fail on silently-skipped suites)`

---

## Task 4: Scheduled weekly full-TFM matrix

**Files (create):** `.github/workflows/scheduled.yml`

- [ ] **Step 1: Create the workflow**

```yaml
name: Scheduled (full TFM matrix)
on:
  schedule:
    # Weekly, Mondays 06:00 UTC. Adds the net10.0 provider coverage the PR matrix omits, and
    # re-verifies every provider against current container images.
    - cron: '0 6 * * 1'
  workflow_dispatch:

jobs:
  integration-full:
    runs-on: ubuntu-latest
    timeout-minutes: 40
    env:
      # Docker is available on GitHub runners; a silently-skipped suite must not pass a scheduled
      # verification run. See tests/Inquiry.IntegrationTesting/DockerRequirement.cs.
      INQUIRY_REQUIRE_DOCKER: "1"
    strategy:
      fail-fast: false
      matrix:
        provider: [PostgreSql, MySql, SqlServer, Oracle]
        tfm: [net8.0, net9.0, net10.0]
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: |
            8.0.x
            9.0.x
            10.0.x
      - name: Integration tests (${{ matrix.provider }} on ${{ matrix.tfm }})
        run: dotnet test tests/Inquiry.${{ matrix.provider }}.Tests/Inquiry.${{ matrix.provider }}.Tests.csproj -c Release -f ${{ matrix.tfm }} --logger "trx;LogFileName=${{ matrix.provider }}-${{ matrix.tfm }}.trx" --results-directory test-results
      - name: Upload integration test results
        if: always()
        uses: actions/upload-artifact@v6
        with:
          name: scheduled-integration-${{ matrix.provider }}-${{ matrix.tfm }}
          path: test-results/
```

- [ ] **Step 2: Verify YAML parses + matches the existing job shape**

Eyeball against `ci.yml`'s `integration` job: same `runs-on`, `timeout-minutes`, `fail-fast: false`, the `dotnet test … -f ${{ matrix.tfm }}` command, and the upload step — differing only by the added `net10.0`, the `schedule`/`workflow_dispatch` triggers, the job-level `env`, and the artifact name prefix. (Run `actionlint .github/workflows/scheduled.yml` if available.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/scheduled.yml
git commit -F <bom-free-msg-file>
```
Message: `ci: add scheduled weekly full provider × net8/9/10 matrix`

---

## Task 5: Final verification

**Files:** none.

- [ ] **Step 1: Non-Docker suites stay green (incl. the new helper test)**

Run: `dotnet test tests/Inquiry.Sqlite.Tests -f net8.0 --nologo` → all pass (includes the 4 `DockerRequirementTests`).
Run: `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --nologo` → all pass (sanity; unaffected).

- [ ] **Step 2: Confirm no stray edits**

Run: `git status --short` → clean. `git diff main...HEAD --stat` → only the helper, the test, the 4 fixtures, `ci.yml`, `scheduled.yml`, and the spec/plan docs.

- [ ] **Step 3: Run the code-review skill** on the branch diff before merge; address any Critical/Important findings. Then finish via `superpowers:finishing-a-development-branch`.

---

## Self-Review (completed)

**Spec coverage:** ✓ DockerRequirement helper + 4-case unit test (Task 1) · guard wired into 4 fixtures (Task 2) · ci.yml env (Task 3) · scheduled.yml 4×3 matrix (Task 4) · non-Docker suites green + local both-paths check (Tasks 1, 2 Step 10, 5). All spec §6 criteria map to a task.

**Placeholder scan:** All code is concrete (helper, test, fixture edits with exact provider strings, both YAML files). Local verification branches on `docker info` so it is actionable in either environment.

**Type/name consistency:** `DockerRequirement.Enforce(isRequired, isAvailable, skipReason)`, `ThrowIfRequiredButUnavailable(isAvailable, skipReason)`, `EnvVarName = "INQUIRY_REQUIRE_DOCKER"` used identically in the helper, the test, the fixtures, and both workflows.
