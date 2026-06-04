# Docs Consolidation into the DocFX Site + Roadmap — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the DocFX site (`docs/site/`) the single home for the project's docs — add a contributor-facing **Develop** section (Project status, Roadmap, Contributing, Adding a provider, Design notes), trim the root README to a concise entry point, reconcile every doc to the current code, and clean up stray/duplicate docs.

**Architecture:** The site is DocFX (markdown content + `toc.yml` nav + auto-generated API ref). We add a new `docs/site/develop/` folder with its own `toc.yml`, wire it into the top-level `docs/site/toc.yml`, dissolve `docs/STATUS.md` into it, distill the relevant bits from `docs/plans/` + `docs/superpowers/`, and update pointers in `README.md` / `CLAUDE.md` / `AGENTS.md`. Docs-only — no code is changed; known issues are *documented* on the Roadmap.

**Tech Stack:** DocFX, Markdown, YAML TOC. Local preview: `docfx docs/site/docfx.json --serve`.

**Branch:** `docs/site-consolidation-roadmap` (already created; spec committed as `1ac12c6`).

**Ground truth (verified 2026-06-04 against current source):**
- TFMs: `net8.0;net9.0;net10.0` on `src/Inquiry` core + test projects; provider runtime libs target `net8.0`. Floor is **.NET 8** (not ".NET 6+").
- CI: single `.github/workflows/ci.yml`; **no `nightly.yml`**. Oracle runs in the **per-PR** integration matrix (`provider: [PostgreSql, MySql, SqlServer, Oracle]`, `tfm: [net8.0, net9.0]`). TRX artifacts emitted; no skip-count gating.
- Packages (current, `Directory.Packages.props`): Microsoft.Data.SqlClient 7.0.1, Npgsql 10.0.3, MySqlConnector 2.6.0, Oracle.ManagedDataAccess.Core 23.26.200, Testcontainers.* 4.12.0, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.8, Microsoft.CodeAnalysis.CSharp **4.8.0 (intentionally held)**.
- New diagnostics since the review: `INQ040` (unknown relation FK), `INQ041` (composite-key child relation), `INQ042` (invalid OrderBy direction). `INQ027` (ProjectionOnSoftDeleteEntity) **retired**.
- DI: `AddInquiry(params Assembly[])` + `AddInquiry(Action<InquiryOptions>, params Assembly[])` exist; `InquiryProviderRegistration.EnsureNoExistingConnectionFactory` throws if two providers are registered.

---

## File Structure

**Create:**
- `docs/site/develop/index.md` — Develop section landing page.
- `docs/site/develop/toc.yml` — Develop section nav.
- `docs/site/develop/project-status.md` — current state (from STATUS.md §1, reconciled).
- `docs/site/develop/roadmap.md` — the Roadmap page (content in Task 4).
- `docs/site/develop/contributing.md` — development process (from STATUS.md §2, reconciled).
- `docs/site/develop/adding-a-provider.md` — from `docs/plans/adding-a-provider.md`.
- `docs/site/develop/design-notes.md` — distilled design record (from `docs/plans/` + `docs/superpowers/`).

**Modify:**
- `docs/site/toc.yml` — add the `Develop` node.
- `docs/site/index.md` — add a Develop link to the "Get started" block.
- `docs/site/articles/providers/oracle.md` — fix "nightly" → PR matrix.
- `docs/site/articles/architecture.md` — absorb any unique deep-dive content from README; reconcile.
- `README.md` — trim to concise entry point; fix `.NET 6+`, "Oracle nightly".
- `CLAUDE.md`, `AGENTS.md` — repoint STATUS.md references to the site.
- `docs/STATUS.md` — replace with a short pointer stub.

**Delete:**
- `docs/prepared-statements.md` — stray duplicate (fold any unique content into the site feature article first).
- `CODE_REVIEW_ACTION_REPORT.md` — open items captured on the Roadmap.

**Leave as archive (reconcile only if a stale fact is user-visible):** `docs/plans/*.md`, `docs/superpowers/**`.

---

## Task 1: Scaffold the Develop section and wire it into the nav

**Files:**
- Create: `docs/site/develop/index.md`, `docs/site/develop/toc.yml`
- Modify: `docs/site/toc.yml`, `docs/site/index.md`

- [ ] **Step 1: Create `docs/site/develop/toc.yml`**

```yaml
- name: Project status
  href: project-status.md
- name: Roadmap
  href: roadmap.md
- name: Contributing
  href: contributing.md
- name: Adding a provider
  href: adding-a-provider.md
- name: Design notes
  href: design-notes.md
```

- [ ] **Step 2: Create `docs/site/develop/index.md`**

```markdown
# Develop

Contributor-facing documentation for working on Inquiry itself.

- **[Project status](project-status.md)** — supported engines, feature matrix, and test status.
- **[Roadmap](roadmap.md)** — known issues, security follow-ups, performance ideas, and planned work.
- **[Contributing](contributing.md)** — how the project is built: the skill-first workflow, TDD, live testing, and CI.
- **[Adding a provider](adding-a-provider.md)** — the append-point checklist for a new database dialect.
- **[Design notes](design-notes.md)** — the compile-time architecture decisions and the design/dependency record.
```

- [ ] **Step 3: Add the Develop node to `docs/site/toc.yml`**

The file currently ends with the `API` node. Append:

```yaml
- name: Develop
  href: develop/
  homepage: develop/index.md
```

- [ ] **Step 4: Add a Develop link to the landing page**

In `docs/site/index.md`, in the "## Get started" list, add as the last bullet:

```markdown
- **[Develop](develop/index.md)** — project status, roadmap, and contributor docs.
```

- [ ] **Step 5: Verify the TOC files are valid YAML and hrefs point at files created in later tasks**

Run: `git status --short docs/site`
Expected: the two new `develop/` files plus the two modified `toc.yml`/`index.md`. (The `project-status.md` etc. hrefs resolve once Tasks 2–6 land; that's expected mid-plan.)

- [ ] **Step 6: Commit**

```bash
git add docs/site/develop/index.md docs/site/develop/toc.yml docs/site/toc.yml docs/site/index.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): scaffold Develop section and wire it into the TOC`

---

## Task 2: Write `develop/project-status.md` (reconciled current state)

**Files:**
- Create: `docs/site/develop/project-status.md`
- Source: `docs/STATUS.md` §1 (reconcile against ground truth above)

- [ ] **Step 1: Write the page.** Pull from STATUS.md §1 but **correct** these facts: Oracle is "Testcontainers (PR matrix)" not "(nightly)"; floor is .NET 8 (net8.0/net9.0/net10.0). Include:
  - One-line description (compile-time-SQL micro-ORM; runtime ships zero SQL).
  - **Supported engines** table (5: Sqlite/SqlServer/PostgreSql/MySql/Oracle) with runtime package, analyzer, and live-test status — Sqlite "in-process (no Docker)"; SqlServer/PostgreSql/MySql/Oracle "Testcontainers (PR CI matrix)".
  - **Feature completeness** — the 13-workstream roadmap is implemented and merged; link to [Design notes](design-notes.md) for the per-workstream record.
  - **Test status** snapshot — keep the per-suite shape from STATUS.md §1 but add a note: "Regenerate counts with `dotnet test`." Do **not** invent counts; carry the last-known table and label it a snapshot with its date, or state counts are regenerated on demand. (Counts in STATUS.md: Generators 165, Runtime 95, SQLite 104, PostgreSql 73, SqlServer 68 (+3 FTS skips), MySql 57, Oracle 51.)
  - A short "Last reconciled: 2026-06-04" line.

- [ ] **Step 2: Verify no stale "nightly"/".NET 6"/".NET 7" strings**

Run (Grep tool): pattern `nightly|\.NET [67]|net6|net7` over `docs/site/develop/project-status.md`
Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add docs/site/develop/project-status.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): add Project status page (reconciled engines/TFM/CI facts)`

---

## Task 3: (folded into Task 4 — Roadmap is the next page)

*(Intentionally merged; see Task 4.)*

---

## Task 4: Write `develop/roadmap.md` (the Roadmap page — full content)

**Files:**
- Create: `docs/site/develop/roadmap.md`

- [ ] **Step 1: Write the page with exactly this content** (verified open items only + a recently-resolved footnote):

````markdown
# Roadmap

> This page lists **open** work only — known issues, security follow-ups, performance ideas, and planned
> enhancements. Resolved items are summarized at the [bottom](#recently-resolved). Nothing here blocks
> `main`: the library builds and every test suite passes.
>
> **Last reconciled against the code:** 2026-06-04.

## Known issues & correctness

- **Relation-shape diagnostics have a coverage gap.** Bad relation metadata is reported as a clean
  build diagnostic — `INQ040` (unknown relation foreign key) and `INQ041` (composite-key child
  relation) — **only when an eager-loading method** (`[InquirySelectAllEager]` /
  `[InquirySelectOneByKeyEager]`) is present on the store. A mistyped collection-relation foreign key on
  a store with *no* eager method can still fail generation with a `NullReferenceException` instead of a
  diagnostic, and a relation pointing to the wrong side has no dedicated diagnostic. *Impact: a confusing
  build failure for a narrow misconfiguration.*

## Security

- **Run a formal security scan.** The code has had a manual, security-oriented review and the raw-SQL
  trust boundary is documented (see [Security](../articles/security.md)). No automated multi-agent
  security scan has been run; that remains a release-bar follow-up. *No vulnerability is currently known
  — generated SQL is parameterized and identifiers come from compile-time metadata.*

## Performance & optimization

- **Document and harden generated-key upsert atomicity.** Generated-key upserts are not single-statement
  on every provider — SQL Server uses an `IF EXISTS` branch and PostgreSQL an `UPDATE`/`INSERT` CTE
  rather than `ON CONFLICT` — so concurrent same-key generated-key upserts can race. The
  *client-supplied-key* path is concurrency-tested on all four networked engines; the generated-key
  path's per-provider guarantees are not yet written down. Consider `HOLDLOCK` / atomic conflict
  primitives where they preserve returning behavior, and document the contract per provider.
- **Prepared-statement benchmark (W4 follow-up).** Quantify `PreparedStatementMode.None` vs `Auto` on
  Npgsql (simple + multi-join) with BenchmarkDotNet; the win depends on connection lifecycle (see
  [Prepared statements](../articles/features/prepared-statements.md)).
- **Array parameters for `IN`.** `Compare.In` predicates rewrite the command text per list cardinality,
  which defeats prepared-statement reuse across list lengths. PostgreSQL `= ANY(@ids)` (and equivalents)
  would keep the SQL constant.

## Planned features & enhancements

- **Full-Northwind test & benchmark coverage.** The suites exercise a representative subset across the
  five engines; replicate the full Northwind entity/relationship surface (all tables, all CRUD + read
  shapes) across ADO.NET / Inquiry / Dapper / EF Core in both tests and benchmarks, so every feature is
  compared apples-to-apples on every entity.
- **Multi-database in one container.** Inquiry binds a single global `IInquiryConnectionFactory` per
  service collection (now enforced — registering two providers throws a clear exception). True
  multi-provider support would require keyed/named factories or per-provider store scopes.
- **Trimming / AOT-safe registration.** `AddInquiry()` discovers generated registrations by reflecting
  over loaded assemblies; an `AddInquiry(params Assembly[])` overload already covers the
  not-yet-loaded-assembly case. A source-generated registration manifest would remove the runtime
  reflection and make the path trimming/AOT-safe.
- **CI hardening.** Add skip-count gating (parse the emitted TRX and fail on unexpected provider-suite
  skips, so a silently-skipped Docker suite can't stay green) and a scheduled full provider × TFM matrix
  (provider integration currently runs on net8.0/net9.0 only). Consider a repo-wide warning-count
  threshold now that the known warning sources are scoped-suppressed.
- **Optional Roslyn bump.** `Microsoft.CodeAnalysis.CSharp` is intentionally held at 4.8.0 to keep the
  analyzer's minimum-SDK floor low; revisit only if a newer Roslyn API is needed.

### Explicitly not planned

- **Migrations Phase B** (schema diff / `ALTER` / versioning) — delegate to DbUp or FluentMigrator;
  Inquiry emits initial `CREATE TABLE` DDL only (`InquiryGeneratedSchema.Ddl`).
- **NoSQL / document engines** (Cosmos DB, MongoDB) — they don't fit a SQL-generating, schema-bound,
  JOIN/eager-loading model.
- **JOIN-based or lazy eager loading** — Inquiry's separate-query eager loading is the recommended
  high-performance pattern by design.

## Recently resolved

Since the 2026-06-03 internal review, the following were fixed (each with regression tests) and are **not**
open:

- **Build / runtime floor:** dropped EOL net6.0/net7.0 (now net8.0/net9.0/net10.0; provider runtimes
  net8.0); upgraded all four provider DB clients (Microsoft.Data.SqlClient 7.0.1, Npgsql 10.0.3,
  MySqlConnector 2.6.0, Oracle.ManagedDataAccess.Core 23.26.200) and Testcontainers 3 → 4.12.
- **Correctness:** closed-transaction handles now throw instead of silently using the non-transactional
  pipeline (the leaky `IInquiryTransaction.Inquiry` property was removed); eager-relation SQL constants
  dedupe by relation property, so two relations to the same child type both emit; the MySQL
  `UseDatabaseDefault` upsert update-branch binds the entity value; `QuerySingleOrDefaultAsync` no longer
  requests `SingleRow` while detecting duplicate rows; pagination arguments are validated
  (`offset >= 0`, `limit`/`pageSize > 0`, `pageSize < int.MaxValue`); malformed `OrderBy` directions are
  diagnosed (`INQ042`); projections are allowed on soft-delete entities and compose the active-row filter
  (`INQ027` retired).
- **Providers:** Oracle ref-cursor detection requires the generated `:rc` bind, so it no longer
  misclassifies ad-hoc PL/SQL.
- **Dependency injection:** `AddInquiry(params Assembly[])` overloads added for stores in
  not-yet-loaded assemblies; registering two providers in one container now fails fast with a clear
  message.
- **Hardening:** sample DB credentials are labeled local-dev-only with an `INQUIRY_SAMPLE_DB` override;
  the known build-warning sources are scoped-suppressed (production projects are warnings-as-errors).
- **CI:** Oracle moved into the per-PR integration matrix (net8.0/net9.0); CI emits TRX artifacts.
````

- [ ] **Step 2: Verify no resolved item is listed as open.**

Run (Grep tool): pattern `SingleRow|INQ027|nightly` over `docs/site/develop/roadmap.md`
Expected: `SingleRow` and `INQ027` appear only under "Recently resolved"; no `nightly`. (Manual read to confirm placement.)

- [ ] **Step 3: Commit**

```bash
git add docs/site/develop/roadmap.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): add Roadmap page (verified open items + resolved footnote)`

---

## Task 5: Write `develop/contributing.md` (development process)

**Files:**
- Create: `docs/site/develop/contributing.md`
- Source: `docs/STATUS.md` §2 (reconcile against ground truth)

- [ ] **Step 1: Write the page** from STATUS.md §2, correcting CI facts. Cover, as prose + a short list:
  - **Skill-first workflow** — brainstorm → spec → writing-plans → execute (subagent-driven / executing-plans); debugging skill for bugs.
  - **Worktrees + parallel agents** for large separable workstreams; the shared generator "hot spine" is edited via a serialized foundation pass first (link [Design notes](design-notes.md)).
  - **TDD** — red generator-emission test (assert the exact emitted `const string`) → implement → integration test (SQLite always-on; other dialects via Testcontainers).
  - **Live testing needs only Docker** — each provider test project link-compiles the Northwind source under its own dialect; tests skip gracefully without Docker.
  - **Code review before merge**; **merge to `main` directly — no PRs**.
  - **Commit messages** — BOM-free file + `git commit -F`, ending with the `Co-Authored-By` trailer.
  - **CI** — `.github/workflows/ci.yml` runs the PR matrix: PostgreSQL, MySQL, SQL Server, **and Oracle** integration suites (net8.0/net9.0) plus the non-Docker unit/generator/SQLite suites; TRX artifacts are uploaded. *(There is no nightly workflow.)*
  - Link to **[Adding a provider](adding-a-provider.md)**.

- [ ] **Step 2: Verify** no "nightly", no "net6/net7".

Run (Grep tool): pattern `nightly|net6|net7|\.NET [67]` over `docs/site/develop/contributing.md`
Expected: no matches.

- [ ] **Step 3: Commit**

```bash
git add docs/site/develop/contributing.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): add Contributing page (reconciled CI/process facts)`

---

## Task 6: Write `develop/adding-a-provider.md` and `develop/design-notes.md`

**Files:**
- Create: `docs/site/develop/adding-a-provider.md` (from `docs/plans/adding-a-provider.md`)
- Create: `docs/site/develop/design-notes.md` (distilled from `docs/plans/README.md` + the workstream specs + `docs/superpowers/`)

- [ ] **Step 1: Read `docs/plans/adding-a-provider.md`** and adapt it into `develop/adding-a-provider.md`: keep the append-point checklist, fix any relative links (the site copy lives under `develop/`, so links to `../../src/...` become GitHub links `https://github.com/JakeOverstreet/inquiry/blob/main/src/...` or are reworded as paths), and ensure the abstract `SqlBuilder` members / new diagnostics list is current (mention `INQ040`/`INQ041`/`INQ042` exist; `INQ027` retired).

- [ ] **Step 2: Write `develop/design-notes.md`** — a distilled design record (not a wholesale copy). Sections:
  - **What Inquiry is** — compile-time-SQL micro-ORM; Roslyn incremental generator; runtime ships zero SQL.
  - **The 13-workstream roadmap (implemented)** — the table from `docs/plans/README.md` (E1–E3, W1–W10) with a one-line description each, noting all are merged. Link the archived specs on GitHub (`https://github.com/JakeOverstreet/inquiry/tree/main/docs/plans`).
  - **The shared "hot spine"** — why naive parallelism fails; the foundation-first pass; that new `SqlBuilder` capabilities are `virtual`-with-base-default where dialect-uniform.
  - **Live-runtime testing & benchmarks** — per-dialect compilation of the shared Northwind source; one Testcontainer per provider suite; the catalog-introspection fidelity guardrail; cross-provider apples-to-apples benchmarks + `SequentialAccess` read streaming. (Distilled from `docs/superpowers/specs/2026-06-01-live-runtime-testing-design.md` and `2026-06-02-test-coverage-and-benchmark-expansion-design.md`.)
  - **Out of scope** — Migrations Phase B, NoSQL, JOIN/lazy eager loading (cross-link the Roadmap's "Explicitly not planned").
  - A closing note: the full design specs are retained in-repo under `docs/plans/` and `docs/superpowers/` as the archived design/dependency record.

- [ ] **Step 3: Verify** both files are valid markdown and internal links resolve to existing pages.

Run: `git status --short docs/site/develop`
Expected: the two new files present.

- [ ] **Step 4: Commit**

```bash
git add docs/site/develop/adding-a-provider.md docs/site/develop/design-notes.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): add Adding-a-provider + Design notes pages (distilled from plans/superpowers)`

---

## Task 7: Reconcile the root README into a concise entry point

**Files:**
- Modify: `README.md`
- Modify (if needed): `docs/site/articles/architecture.md`

- [ ] **Step 1: Diff README deep-dive vs `architecture.md`.** Read both. The site's `architecture.md` is the canonical deep-dive. If README contains any architecture detail **not** present in `architecture.md` (e.g., the Flow 1/2/3 walkthrough, the generator-output summary, the builder table), move that content into `architecture.md` so nothing is lost.

- [ ] **Step 2: Rewrite `README.md`** to a concise entry point:
  - One-paragraph description — fix the floor: "**.NET 8+** source-generated micro-ORM" (was ".NET 6+").
  - A short quickstart (the entity + store + `AddInquiry().AddInquirySqlite(...)` snippet already in README is fine, trimmed).
  - A "Documentation" section linking into the site: getting-started, features, providers, security, API reference, and **Develop** (status/roadmap/contributing). Keep the local-preview line (`docfx docs/site/docfx.json --serve`).
  - A short "Repository layout" table may stay (it's useful on GitHub), but drop the long Flow 1/2/3 deep-dive (now in `architecture.md`).
  - Fix the CI sentence: "CI runs PostgreSQL / MySQL / SQL Server **and Oracle** on every PR" (was "Oracle nightly").
  - Replace the "see `docs/STATUS.md`" pointers with the site (Develop → Project status / Roadmap).

- [ ] **Step 3: Verify** README no longer claims ".NET 6", "nightly", or points to STATUS.md as the status source.

Run (Grep tool): pattern `\.NET 6|nightly|STATUS\.md` over `README.md`
Expected: no matches (or `STATUS.md` only if you keep a single "history" mention — prefer none).

- [ ] **Step 4: Commit**

```bash
git add README.md docs/site/articles/architecture.md
git commit -F <bom-free-msg-file>
```
Message: `docs: trim README to a concise entry point; fix .NET floor + Oracle-CI facts`

---

## Task 8: Reconcile the site articles against the code

**Files:**
- Modify: `docs/site/articles/providers/oracle.md` (+ any article a check flags)

- [ ] **Step 1: Fix the Oracle "nightly" claim.** In `docs/site/articles/providers/oracle.md` (~line 46) change the "(nightly CI; ~3 min container warm-up)" wording to reflect that Oracle runs in the per-PR integration matrix.

- [ ] **Step 2: Spot-check feature/provider articles against verified behaviors.** Read each and confirm the prose matches the code; fix any mismatch. Concretely verify:
  - `features/soft-delete.md` — projections are **allowed** on soft-delete entities (don't claim they're blocked).
  - `features/pagination.md` — keyset uses two queries (a sargable seek + a first-page query); pagination args are validated.
  - `features/concurrency.md` — covers optimistic concurrency (`[InquiryRowVersion]`); it need not cover upsert atomicity (that's a Roadmap item) but must not contradict it.
  - `features/batch-operations.md` — Oracle `InsertAll` is real (`INSERT ALL`); `UpdateAll` is unsupported on Oracle (`INQ039`).
  - `features/prepared-statements.md` — matches `docs/prepared-statements.md` content (Task 9 deletes the stray); note `DbType.DateTime2` for `System.DateTime`.
  - `providers/*.md` — upsert strategy per dialect matches the builders (SQLite/PostgreSQL `ON CONFLICT`, SQL Server/Oracle `MERGE`, MySQL `ON DUPLICATE KEY UPDATE`).
  - `architecture.md` / `security.md` — `security.md`'s `INQ042` mention is **correct** (verified); leave it.

- [ ] **Step 3: Verify** no remaining stale TFM/CI strings across the site.

Run (Grep tool): pattern `nightly|net6|net7|\.NET [67]` over `docs/site`
Expected: no matches.

- [ ] **Step 4: Commit**

```bash
git add docs/site/articles
git commit -F <bom-free-msg-file>
```
Message: `docs(site): reconcile provider/feature articles with current code`

---

## Task 9: Dissolve STATUS.md, remove stray/duplicate docs, repoint CLAUDE/AGENTS

**Files:**
- Modify: `docs/STATUS.md` (→ stub), `CLAUDE.md`, `AGENTS.md`
- Delete: `docs/prepared-statements.md`, `CODE_REVIEW_ACTION_REPORT.md`

- [ ] **Step 1: Confirm `docs/prepared-statements.md` is fully covered** by `docs/site/articles/features/prepared-statements.md`. Read both; if the stray has any unique content (e.g., the Npgsql connection-string guidance, the `Compare.In` caveat, the `DbType.DateTime2` note), ensure it's present in the site article (add it if missing), then delete the stray.

```bash
git rm docs/prepared-statements.md
```

- [ ] **Step 2: Delete the review report** (its open items are on the Roadmap, resolved items in the footnote).

```bash
git rm CODE_REVIEW_ACTION_REPORT.md
```
Note: this file is currently **untracked**; if `git rm` errors, use a plain file delete instead.

- [ ] **Step 3: Replace `docs/STATUS.md` with a stub** pointing at the site:

```markdown
# Inquiry — Project Status & Onboarding

> **Moved.** Project status, the development process, and the roadmap now live in the documentation
> site under **Develop**:
>
> - **Project status:** [`site/develop/project-status.md`](site/develop/project-status.md)
> - **Roadmap:** [`site/develop/roadmap.md`](site/develop/roadmap.md)
> - **Contributing / process:** [`site/develop/contributing.md`](site/develop/contributing.md)
> - **Design notes:** [`site/develop/design-notes.md`](site/develop/design-notes.md)
>
> Build the site locally with `docfx docs/site/docfx.json --serve` (see [`site/README.md`](site/README.md)).
> The architecture deep-dive is in the site's Architecture article; behavioral coding guidelines are in
> [`../CLAUDE.md`](../CLAUDE.md).
```

- [ ] **Step 4: Repoint `CLAUDE.md`.** Its header block references `docs/STATUS.md` as "the onboarding source of truth." Update that pointer to the site's Develop area (Project status + Roadmap), keeping the stub as a fallback link.

- [ ] **Step 5: Repoint `AGENTS.md`** the same way if it references STATUS.md (read it first; mirror the CLAUDE.md change).

- [ ] **Step 6: Verify** nothing still calls STATUS.md the source of truth and the deleted files are gone.

Run: `git status --short` and Grep pattern `STATUS\.md` over `CLAUDE.md AGENTS.md README.md`
Expected: deletions staged; STATUS.md references are now stub/pointer-style, not "source of truth".

- [ ] **Step 7: Commit**

```bash
git add docs/STATUS.md CLAUDE.md AGENTS.md
git commit -F <bom-free-msg-file>
```
Message: `docs: dissolve STATUS.md into the site Develop area; drop stray review/duplicate docs`

---

## Task 10: Validate the site build and finalize

**Files:** none (validation + optional fixups)

- [ ] **Step 1: Check whether DocFX is installed.**

Run: `docfx --version`
- If installed → Step 2.
- If not → skip the build; do the manual link check in Step 3 instead, and note in the final summary that a DocFX build wasn't run.

- [ ] **Step 2: Build the site and inspect warnings.**

Run: `docfx docs/site/docfx.json`
Expected: build succeeds. Scan output for `warning` lines about invalid file links / missing TOC hrefs / broken xref. Fix any that point at the new `develop/` pages or changed links, then re-run.

- [ ] **Step 3: Manual link check** (always do this).

Grep the new `develop/*.md` and the rewritten `README.md` for markdown links; confirm each relative target exists. Pay attention to depth: from `docs/site/develop/`, links to articles are `../articles/...`; links to API are `../api/...`.

- [ ] **Step 4: Final reconciliation sweep.**

Run (Grep tool) over the whole repo excluding the archive: pattern `\.NET 6\+|Oracle nightly|nightly\.yml` over `README.md docs/site`
Expected: no matches. (Archived `docs/plans/` / `docs/superpowers/` may retain historical mentions — acceptable.)

- [ ] **Step 5: Commit any validation fixups.**

```bash
git add -A docs/site README.md
git commit -F <bom-free-msg-file>
```
Message: `docs(site): fix links/TOC surfaced by DocFX build`
(Skip if Step 2–4 produced no changes.)

- [ ] **Step 6: Run the superpowers code-review skill** on the branch diff (per project process) before merge; address any Critical/Important findings.

---

## Self-Review (run after writing the plan — completed)

**Spec coverage:** ✓ Develop section (Tasks 1,2,4,5,6) · Roadmap by-category open-only (Task 4) · concise README (Task 7) · reconcile docs↔code (Tasks 2,5,7,8) · STATUS stub + CLAUDE/AGENTS repoint (Task 9) · delete stray/review docs (Task 9) · DocFX validation (Task 10). All spec §9 success criteria map to a task.

**Placeholder scan:** Roadmap content is fully written (Task 4). Pages that are distilled/reconciled (Tasks 2,5,6,7) give explicit section lists + exact sources + the verified facts to use — no "TBD". Counts in Task 2 are carried from STATUS.md and labeled a dated snapshot (not invented).

**Type/name consistency:** Diagnostic IDs (`INQ040/041/042`, retired `INQ027`), package versions, TFMs, and the Oracle-in-PR-matrix fact are used identically across tasks and match the verified ground truth block.
