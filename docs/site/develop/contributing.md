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
runs on every pull request and on pushes to `main`:

- a **build-and-unit** job — the generator, runtime, and SQLite suites (no Docker); and
- an **integration** job — a matrix of **PostgreSQL, MySQL, SQL Server, and Oracle** live suites, each on
  **net8.0** and **net9.0**, provisioned with Testcontainers.

CI uploads TRX result artifacts (`if: always()`) so failures and skips can be inspected. There is no
separate nightly workflow — Oracle runs in the per-PR matrix alongside the other engines.

## Adding a database

See [Adding a provider](adding-a-provider.md) for the append-point checklist.
