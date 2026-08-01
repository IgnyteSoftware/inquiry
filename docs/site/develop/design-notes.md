# Design notes

The condensed design and dependency record. For the user-facing architecture write-up see
[Architecture](../articles/architecture.md); the full per-workstream specs are retained in-repo under
[`docs/plans/`](https://github.com/IgnyteSoftware/inquiry/tree/main/docs/plans) and
[`docs/superpowers/`](https://github.com/IgnyteSoftware/inquiry/tree/main/docs/superpowers).

## What Inquiry is

A compile-time-SQL micro-ORM: a Roslyn incremental source generator that bakes every SQL statement as a
`const string` at build time and emits zero-allocation struct materializers. The runtime ships no SQL and no
dialect type — each provider analyzer produces the right SQL flavor for its dialect when the compilation's
`[InquiryDialect]` matches.

## The feature roadmap (implemented)

The original 13-workstream roadmap is built and merged to `main`. It was sequenced to run as parallel git
worktrees against a shared generator core:

| ID | Workstream | ID | Workstream |
|----|-----------|----|-----------|
| E1 | MySQL / MariaDB provider | W5 | Projections + aggregations |
| E2 | Oracle provider | W6 | Optimistic concurrency / row-versioning |
| E3 | Cloud-compat (Azure SQL / CockroachDB / Aurora retry) | W7 | Migrations / schema DDL (Phase A) |
| W1 | Richer WHERE predicates | W8 | Soft deletes |
| W2 | ORDER BY + pagination (offset + keyset) | W9 | Full-text search |
| W3 | Batch & bulk operations | W10 | JSON / array / value-converter columns |
| W4 | Automatic prepared-statement reuse | | |

Each has a user-facing article under [Features](../articles/features/crud.md). Remaining follow-ups are on
the [Roadmap](roadmap.md).

## The shared "hot spine"

Nearly every workstream edits the same handful of generator files — the `SqlBuilder` hierarchy (+ each
`*SqlBuilder`), `StoreProcessor`, `StoreOperationEmitter`, `EntityProcessor`, the column/operation models,
and the diagnostic registry. Naive parallelism collides there. The project's answer:

- **Foundation-first.** A serialized foundation pass turned the hot spine into stable extension points
  (init-only column metadata added additively; a single `AppendWhere` primitive through which key,
  concurrency, soft-delete, and filter predicates AND-compose; a shared `MaterializerEmitter`; a reserved
  diagnostic-ID registry). Only then do the feature workstreams fan out.
- **`virtual`-with-base-default over `abstract`.** New `SqlBuilder` capabilities default in the base where
  the SQL is dialect-uniform, so a new provider inherits them and overrides only what differs. An `abstract`
  member breaks every provider's build until implemented, so it's used only when there's no portable default.
- **Append-only edits** to the operation/diagnostic enums and registration arrays, so parallel branches
  merge textually. See [Adding a provider](adding-a-provider.md) for the append-point checklist.

## Live-runtime testing & benchmarks

- **Per-dialect compilation.** Each provider test project link-compiles the shared Northwind source under
  *its own* analyzer, so it tests that engine's real generated SQL — not a mock.
- **Testcontainers, skip-without-Docker.** One container per provider test assembly (via Testcontainers);
  every live fact uses `SkippableFact` and skips gracefully when Docker is absent.
- **Schema-fidelity guardrail.** `Inquiry.IntegrationTesting` holds the canonical expected-Northwind schema,
  a schema-fidelity comparator, and an `ISchemaIntrospector` per engine; the generated DDL
  (`InquiryGeneratedSchema.Ddl`) is verified to stand up on each live engine and to match the expected
  structure (tables + primary keys + foreign keys).
- **Apples-to-apples benchmarks.** BenchmarkDotNet suites compare ADO.NET / Inquiry / Dapper / EF Core on
  the same operations, in-process on SQLite and per-dialect (PostgreSQL / MySQL / SQL Server / Oracle) over
  Testcontainers. The generated-store read overloads stream with `CommandBehavior.SequentialAccess`
  (generated materializers read each column once, in ascending order), bringing large-read allocation to
  raw-ADO levels. Headline: Inquiry allocates at raw-ADO levels (lowest of any wrapper); EF Core is several
  times higher.

## Out of scope (by design)

- **Migrations Phase B** (schema diff / `ALTER` / versioning) — delegate to DbUp or FluentMigrator; Inquiry
  generates the initial `CREATE TABLE` DDL only.
- **NoSQL / document engines** (Cosmos DB, MongoDB) — they don't fit a SQL-generating, schema-bound model.
- **JOIN-based or lazy eager loading** — the separate-query model is the recommended high-performance
  pattern; it is not considered a gap.

See the [Roadmap](roadmap.md#explicitly-out-of-scope-for-10) for the same not-planned list in context.
