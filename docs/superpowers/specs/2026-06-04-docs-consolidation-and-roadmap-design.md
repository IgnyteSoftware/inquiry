# Design — Docs Consolidation into the DocFX Site + Roadmap Page

- **Date:** 2026-06-04
- **Status:** Approved (scope confirmed via scoping questions — see §2).
- **Owner:** in-session work, project process (brainstorm → spec → plan → execute).

## 1. Goal

Make the DocFX site (`docs/site/`) the single home for the project's documentation, add a **Roadmap**
page that captures all known-open future work (bugs, security, optimization, planned features), and
**reconcile every doc against the current code** so nothing is stale. After this lands we decide next
steps; **no code is fixed here** — issues are *documented*, not resolved.

Concrete outcomes:

1. A new contributor-facing **Develop** section in the site (Project status, Roadmap, Contributing,
   Adding a provider, Design notes), built by dissolving `docs/STATUS.md` and distilling the *relevant*
   data from `docs/plans/` and `docs/superpowers/`.
2. A **Roadmap** page organized by category (Known issues/bugs · Security · Performance · Planned
   features), **open items only**, with a short "recently resolved" footnote.
3. The root `README.md` trimmed to a concise entry point that links into the site.
4. Every existing site article verified against the code; stale facts corrected.
5. Stray/duplicate docs cleaned up; `CLAUDE.md`/`AGENTS.md` pointers updated.

## 2. Scope decisions (from scoping questions)

- **Consolidation = hybrid.** STATUS.md content moves *into* the site. A **Develop** area adds only the
  *relevant* distilled data from `plans/` and `superpowers/` (not a wholesale render of all 14 specs).
  Update `CLAUDE.md` and `README.md` pointers.
- **Root README = concise entry point.** Overview + quickstart + links into the site; the architecture
  deep-dive is reconciled into the site's Architecture article.
- **Roadmap = by category, open items only**, plus a brief "recently resolved" footnote.
- **Reconcile depth = verify each item.** Every `CODE_REVIEW_ACTION_REPORT.md` finding is checked
  against current source before it is classified open vs. resolved.

## 3. Current state (doc inventory)

- **`docs/site/`** — DocFX site, already fairly complete: landing `index.md`, `api/index.md`,
  `articles/{getting-started,concepts,architecture,security}.md`, `articles/features/` (15),
  `articles/providers/` (5), TOCs. `_site/` and `api/*.yml` are gitignored. **This is the target.**
- **`README.md`** (root) — full architecture deep-dive. Known stale: *"experimental .NET 6+"* (net6/net7
  were dropped — should be .NET 8+).
- **`docs/STATUS.md`** — onboarding source of truth: §1 current state, §2 dev process, §3 upcoming work.
- **`docs/prepared-statements.md`** — stray near-duplicate of
  `docs/site/articles/features/prepared-statements.md`.
- **`docs/plans/`** (14) — 13-workstream design specs (all marked IMPLEMENTED), roadmap `README.md`,
  `adding-a-provider.md`.
- **`docs/superpowers/`** — 2 specs + 2 plans (process artifacts).
- **`CLAUDE.md` / `AGENTS.md`** — agent behavioral guidelines (root); reference STATUS.md.
- **`CODE_REVIEW_ACTION_REPORT.md`** (untracked, dated 2026-06-03) — P1/P2/P3 review. Roadmap seed;
  partly stale (lists package upgrades, sample-cred labeling, net6/net7 test runs already done; lists
  P3 #14 projections-on-soft-delete as open though git shows it resolved in `a1c5c50`).
- **`samples/**/README.md`, `benchmarks/**/README.md`** — contextual; stay in place. Benchmark result
  `.md` files live under gitignored `BenchmarkDotNet.Artifacts/` — not docs.

## 4. Target information architecture

```
Docs (articles/)          ← user-facing, structure unchanged
  Getting started · How it works · Architecture · Security
  Features/ (15)  ·  Providers/ (5)
API (api/)                ← unchanged
Develop (develop/)        ← NEW top-level nav node, own toc.yml
  Project status          ← STATUS.md §1 (engines, feature matrix, test status), reconciled
  Roadmap                 ← NEW (see §5)
  Contributing            ← STATUS.md §2 (skill-first, worktrees, TDD, Docker/live testing, CI, merge)
  Adding a provider       ← docs/plans/adding-a-provider.md
  Design notes            ← distilled from docs/plans/ + docs/superpowers/: compile-time architecture
                            decisions, hot-spine rationale, out-of-scope calls (NoSQL/JOIN/Migrations
                            Phase B), live-testing + benchmark approach
```

`docs/site/toc.yml` gains a third node (`Develop` → `develop/`, homepage `develop/index.md`).
`docs/site/develop/toc.yml` lists the five pages above. The landing `index.md` "Get started" block gains
a link to the Develop area.

## 5. The Roadmap page (`develop/roadmap.md`)

By category, **open items only**, each item a short title + one-line impact + source pointer
(`file:line`) so it stays actionable. Seeded from `CODE_REVIEW_ACTION_REPORT.md` + STATUS.md §3,
**after verifying each against current source.**

- **Known issues & correctness bugs** — verified-open items from P1/P2, e.g. closed-transaction
  `tx.Inquiry` routing (P1 #1), eager-relation SQL dedup by child *type* not relation (P1 #2),
  relation-metadata diagnostics (P1 #3), MySQL `UseDatabaseDefault` upsert branch (P1 #4),
  `QuerySingleOrDefaultAsync` requesting `SingleRow` while detecting duplicates (P2 #5), Oracle ref-cursor
  prefix heuristic (P2 #7), malformed `OrderBy` accepted silently (P2 #13), pagination args lack runtime
  validation (P2 #12).
- **Security** — formal multi-agent security scan follow-up (P3 #17); cross-link the existing Security
  article's raw-SQL trust boundary (no new vuln found).
- **Performance / optimization** — prepared-statement `None`-vs-`Auto` benchmark follow-up, array-param
  `IN` (`= ANY(@ids)`) to keep SQL constant for prepared reuse, upsert concurrency/atomicity (P2 #6).
- **Planned features & enhancements** — full-Northwind test/benchmark coverage (STATUS §3 G #18),
  `AddInquiry(params Assembly[])` overload + separate-assembly store test (P2 #8), multi-provider DI
  (keyed factories) (P2 #9), CI skip-count gating + scheduled full TFM matrix (P2 #10), build-warning
  baseline cleanup (P2 #11). Plus an **"explicitly not planned"** note: Migrations Phase B (delegate to
  DbUp/FluentMigrator), NoSQL/document engines, JOIN-based/lazy eager loading.
- **Recently resolved** (footnote) — net6/net7 drop + provider-client/Testcontainers upgrades (P3 #16),
  sample-credential labeling (P3 #15), projections-on-soft-delete (P3 #14), and the live-testing-era
  Oracle/keyset/PostgreSQL fixes from STATUS §3.

Each item that verification shows already-resolved is dropped from the open list (and may appear in the
footnote). Each item kept open must still be true against current source.

## 6. Reconciliation pass (docs ↔ code)

Verify and correct, concretely:

- **TFM / dialect facts** — `.NET 6+` → `.NET 8+` in README; any net6/net7 mention in site articles.
- **Feature availability & status** — every feature article reflects what the code actually supports
  (e.g. projections now allowed on soft-delete entities; Oracle batch insert real, `UpdateAll`
  unsupported via INQ039; keyset two-query seek/first-page shape).
- **Names referenced in prose** — attribute names, method names, diagnostic IDs (INQxxx), package
  versions, test counts that appear in text.
- **Each review-report item** — checked against current source before open/resolved classification.

Verification of the ~17 independent review items runs via parallel read-only subagents; all writing and
restructuring is done inline.

## 7. File-by-file disposition

| Doc | Disposition |
|---|---|
| `docs/site/**` | Target. Reconcile every article vs. code; add `develop/` + TOC entries. |
| `README.md` | Trim to concise entry point (overview + quickstart + links); fix `.NET 6+`; deep-dive reconciled into site Architecture. |
| `docs/STATUS.md` | Content → site Develop (Status + Contributing + Roadmap). File → short pointer stub to the site. |
| `docs/prepared-statements.md` | Verify redundant vs. site feature article, fold any unique bit in, **delete**. |
| `docs/plans/*.md` (specs) | Keep as internal design archive; distill relevant data into Develop → Design notes; fix stale context lines. |
| `docs/plans/adding-a-provider.md` | Source for Develop → Adding a provider. |
| `docs/plans/README.md` | Keep as archive index; its "all-done" history feeds the Roadmap footnote / Design notes. |
| `docs/superpowers/**` | Keep as archive; distill relevant approach notes into Develop. |
| `CLAUDE.md` / `AGENTS.md` | Update doc pointers (STATUS → site). Stay at root. |
| `CODE_REVIEW_ACTION_REPORT.md` | Verify each item, fold open ones into Roadmap, then **delete** the root file. |
| `samples/**/README.md`, `benchmarks/**/README.md` | Keep in place; link from the site. |

## 8. Out of scope

- **No code fixes.** Issues are documented on the Roadmap, not resolved.
- **No wholesale render** of the 14 plan specs / superpowers specs as public pages — only distilled
  relevant data enters the site.
- **No hosting/deploy changes** — the site stays local-preview (`docfx docs/site/docfx.json --serve`).

## 9. Success criteria

1. `docs/site/` has a working **Develop** section (Project status, Roadmap, Contributing, Adding a
   provider, Design notes) wired into the TOC.
2. **Roadmap** page exists, by-category, open-items-only, every open item verified true against source,
   resolved items dropped.
3. Root `README.md` is a concise entry point; `.NET 6+` and other stale facts corrected.
4. `docs/prepared-statements.md` and `CODE_REVIEW_ACTION_REPORT.md` removed after their content is
   folded in; `STATUS.md` is a stub pointing to the site.
5. `CLAUDE.md` / `AGENTS.md` / `README.md` point at the site for status/roadmap.
6. No remaining doc claims contradict the code (TFM, feature status, names, counts spot-checked).
7. DocFX builds with no broken links / TOC errors (if `docfx` is installed; else links validated
   manually).
