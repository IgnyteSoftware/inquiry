# Roadmap

> This page lists **open** work only — known issues, security follow-ups, performance ideas, and planned
> enhancements. Resolved items are summarized at the [bottom](#recently-resolved). Nothing here blocks
> `main`: the library builds and every test suite passes.
>
> **Last reconciled against the code:** 2026-06-04.

## Known issues & correctness

- *No open correctness bugs are currently known.* (The relation-const generator crash previously listed
  here is fixed — see [Recently resolved](#recently-resolved). A residual *diagnostics* gap — relation
  typos are only reported when the relation is eager-loaded — is tracked under
  [Planned features](#planned-features--enhancements).)

## Security

- **Run a formal security scan.** The code has had a manual, security-oriented review and the raw-SQL
  trust boundary is documented (see [Security](../articles/security.md)). No automated multi-agent
  security scan has been run; that remains a release-bar follow-up. *No vulnerability is currently known
  — generated SQL is parameterized and identifiers come from compile-time metadata.*

## Performance & optimization

- **Harden generated-key upsert atomicity.** On SQL Server and PostgreSQL the generated-key upsert is
  not single-statement — SQL Server uses an `IF EXISTS` branch and PostgreSQL an `UPDATE`/`INSERT` CTE —
  so concurrent same-key generated-key upserts can race there. (SQLite and MySQL use a single atomic
  `ON CONFLICT` / `ON DUPLICATE KEY` statement.) The per-provider contract is already documented (see
  [CRUD § Upsert concurrency](../articles/features/crud.md#upsert-concurrency-semantics)) and the
  client-supplied-key path is concurrency-tested on all four networked engines; the remaining work is to
  *harden* the SQL Server and PostgreSQL generated-key paths — e.g. `HOLDLOCK` or atomic conflict
  primitives where they preserve returning behavior.
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
- **Providers:** Oracle ref-cursor detection requires the generated `:rc` bind, so it no longer
  misclassifies ad-hoc PL/SQL.
- **Dependency injection:** `AddInquiry(params Assembly[])` overloads added for stores in
  not-yet-loaded assemblies; registering two providers in one container now fails fast with a clear
  message.
- **Hardening:** sample DB credentials are labeled local-dev-only with an `INQUIRY_SAMPLE_DB` override;
  the known build-warning sources are scoped-suppressed (production projects are warnings-as-errors).
- **CI:** Oracle moved into the per-PR integration matrix (net8.0/net9.0); CI emits TRX artifacts.
- **CI hardening:** a provider suite that can't start its Docker container now FAILS CI (via the
  `INQUIRY_REQUIRE_DOCKER` guard) instead of silently skipping; a new scheduled weekly workflow runs the
  full provider × net8.0/net9.0/net10.0 matrix (PR CI stays net8.0/net9.0).
- **Generator robustness:** a mistyped collection-relation foreign key on a store with no eager method no
  longer crashes the generator (`NullReferenceException`) — relation SELECT consts are emitted only when a
  valid eager method consumes them; a bad relation that *is* eager-loaded still reports `INQ040`/`INQ041`.
