# Roadmap

> This page lists **open** work only — known issues, security follow-ups, performance ideas, and planned
> enhancements. Resolved items are summarized at the [bottom](#recently-resolved). Nothing here blocks
> `main`: the library builds and every test suite passes.
>
> **Last reconciled against the code:** 2026-06-06.

## Known issues & correctness

- *No open correctness bugs are currently known.* (The relation-const generator crash previously listed
  here is fixed — see [Recently resolved](#recently-resolved). A residual *diagnostics* gap — relation
  typos are only reported when the relation is eager-loaded — is tracked under
  [Planned features](#planned-features--enhancements).)

## Security

- *No open security follow-ups are currently known.* The formal Codex Security repository scan has been run;
  validated findings were fixed in `318ee5f` and summarized in [Security](../articles/security.md).

## Performance & optimization

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
- **CI: repo-wide warning gate.** Production projects are warnings-as-errors and the known warning
  sources are scoped-suppressed; a repo-wide build-warning gate (extending coverage to the test projects)
  would catch new warnings. *(Skip-gating and the scheduled full-TFM matrix are done — see
  [Recently resolved](#recently-resolved).)*
- **Optional Roslyn bump.** `Microsoft.CodeAnalysis.CSharp` is intentionally held at 4.8.0 to keep the
  analyzer's minimum-SDK floor low; revisit only if a newer Roslyn API is needed.
- **Broaden relation-shape diagnostics.** `INQ040` (unknown relation foreign key) and `INQ041`
  (composite-key child) fire only when an eager-loading method traverses the relation. A relation that is
  mistyped but never eager-loaded is silently skipped (it no longer crashes the generator), and a foreign
  key pointing at the wrong side has no dedicated diagnostic. Report these at declaration time regardless
  of eager usage. *Low severity — no crash, and no wrong results unless the relation is eager-loaded.*

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
- **Upsert atomicity & generated-key parity (all relational engines except Oracle):** generated-key upserts
  are atomic — SQL Server uses `MERGE … WITH (HOLDLOCK)` (client and generated key), PostgreSQL uses
  `INSERT … ON CONFLICT` — so concurrent same-key upserts no longer throw a spurious duplicate-key error;
  covered by live concurrency + `uniqueidentifier`/`gen_random_uuid()` key tests. SQLite + MySQL parity is
  now **test-proven** (live generate + concurrency tests). MySQL additionally supports a **database-generated
  GUID key**: a `Guid?` `UseDatabaseDefault` key is generated server-side via `UUID()` (captured in a
  `@_inquiry_genkey` user variable for the emulated returning), so Inquiry enables `AllowUserVariables=true`
  on MySQL connections by default. (Oracle generated-key upsert remains unsupported, tracked separately.)
- **Providers:** Oracle ref-cursor detection requires the generated `:rc` bind, so it no longer
  misclassifies ad-hoc PL/SQL.
- **Dependency injection:** generated `AddInquiryGeneratedStores()` registration is explicit, so
  `AddInquiry()` no longer scans loaded AppDomain assemblies by default. The
  `AddInquiry(params Assembly[])` fallback remains for intentional assembly-based registration, and
  registering two providers in one container now fails fast with a clear message.
- **Hardening:** sample DB credentials are labeled local-dev-only with an `INQUIRY_SAMPLE_DB` override;
  the known build-warning sources are scoped-suppressed (production projects are warnings-as-errors).
- **CI:** Oracle moved into the integration matrix (net8.0/net9.0); CI emits TRX artifacts.
- **CI hardening:** a provider suite that can't start its Docker container now FAILS CI (via the
  `INQUIRY_REQUIRE_DOCKER` guard) instead of silently skipping; a new scheduled weekly workflow runs the
  full provider × net8.0/net9.0/net10.0 matrix (the normal integration matrix stays net8.0/net9.0).
- **Formal security scan:** the Codex Security repository scan completed during pre-release hardening.
  Findings were fixed with regression coverage for lazy batch parameter-cap enforcement, MySQL
  update-returning concurrency behavior, and Oracle generated bind-name collisions.
- **Prepared-statement benchmark:** the PostgreSQL BenchmarkDotNet harness compares
  `PreparedStatementMode.None` vs `Auto` on Npgsql for a generated simple point read and a stable ad-hoc
  multi-join point read. The 2026-06-06 full run measured lower means for `Auto` in both categories
  (multi-join: 713.5 us vs 944.5 us; simple point read: 587.9 us vs 662.6 us), with BDN distribution
  warnings appropriate for networked container benchmarks.
- **Generator robustness:** a mistyped collection-relation foreign key on a store with no eager method no
  longer crashes the generator (`NullReferenceException`) — relation SELECT consts are emitted only when a
  valid eager method consumes them; a bad relation that *is* eager-loaded still reports `INQ040`/`INQ041`.
- **Pre-release API hardening:** high-level ad-hoc SQL APIs now use safe `FormattableString`
  overloads instead of raw `string` command text, with `InquiryCommand` left as the explicit
  advanced escape hatch. `IInquiry.ExecuteInTransactionAsync(...)` now owns the common
  begin/commit/rollback transaction flow. Runtime implementation types, provider connection factories,
  retry helpers, and request pipelines are internal, and generated-code-only support contracts are
  hidden from IntelliSense where they must remain public for source generation.
