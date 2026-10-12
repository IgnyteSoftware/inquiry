# Design notes

The condensed design and dependency record. For the user-facing architecture write-up see
[Architecture](../articles/architecture.md). The original per-workstream design specs are no longer
carried in the tree; they remain in git history. Decisions about the 1.0 public API for stores,
predicate binding, result mapping, and stored procedures are in
[1.0 API contract decisions](api-contract-decisions.md).

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

## Diagnostic suppression and unsupported stubs (#268)

Status: proposed. Merging the pull request that adds this section accepts it. The user-facing rules
stay in the [Diagnostic repair index](../articles/diagnostics.md#severity-and-suppression) and change
only when the follow-up work lands.

### Context

Most Inquiry diagnostics are errors that reject a declaration. A rejected partial method has no
generated body, so the compiler also reports CS8795. To reduce that noise, some paths emit stubs that
throw `NotSupportedException`. The paths do not agree on when a stub is allowed:

| Path | Default build | Rejecting error suppressed in the project |
|------|---------------|-------------------------------------------|
| INQ039 operation the dialect cannot emit (`StoreProcessor.cs:2235-2246`) | Fails: INQ039 + CS8795 | Stub only if INQ039 is lowered project-wide |
| Provider facet error, e.g. INQ076 (`StoreProcessor.cs:2226-2234`) | Fails: INQ076 + CS8795 | Stub only if INQ039 is also lowered |
| Some methods rejected, others valid | Fails: error + CS8795 | Fails: CS8795 |
| Every method rejected (`StoreProcessor.cs:1456-1471`) | Fails: error only | **Builds.** Stubs, DI registration |
| Store entity unmapped or invalid (`StoreProcessor.cs:1344-1361`) | Fails: INQ008 only | **Builds.** Stubs, DI registration |
| No method survived extraction (`StoreProcessor.cs:1461-1464`) | Fails: error + CS8795 | Fails: CS8795 |
| Unsupported token or token upsert (`StoreProcessor.cs:1477-1487`) | Fails: error + CS8795 | Fails: CS8795 |

The two bold rows use `EmitStubOnlyStore` (`StoreProcessor.cs:2326-2349`). It ignores the INQ039
opt-in, and `AddInquiryGeneratedStores` registers the result (`RegistrationEmitter.cs:50-65`).
`WriteEnforcedFilterGeneratorTests.UpsertRejectionFailsClosedWhenTheDiagnosticIsSuppressed` pins
this today. The opt-in check reads compilation and `.editorconfig` severity for INQ039 only, and
requires the same value for every syntax tree (`InquiryGeneratorBase.cs:653-693`).

Suppression mechanisms behave differently. A local probe against the SQLite generator (.NET SDK
10.0.400) showed:

- `<NoWarn>` and `.editorconfig`/`.globalconfig` severity `none` or `warning` remove the INQ012
  error. The all-rejected store then builds, and its generated stub store is registered.
- `#pragma warning disable` around the declaration does not remove INQ012, at error or warning
  severity. Pragmas do not affect these generator diagnostics.
- A descriptor tagged `WellKnownDiagnosticTags.NotConfigurable` ignores both `<NoWarn>` and
  `.editorconfig`.

This is not a security bypass. The stub path needs a deliberate project-level suppression of an
error. It is an inconsistency in the unsupported-operation contract, and that contract freezes at 1.0.

### Options considered

1. **Document the current behavior.** No code change. Suppressing any error on an all-rejected or
   invalid-entity store keeps producing a registered stub store. The rules stay path-dependent, and
   the documented claim that INQ039 is the only stub opt-in stays false.
2. **Stubs only for INQ039 (recommended).** The INQ039 opt-in stubs only methods that report INQ039.
   Every other rejection leaves the method without a generated body, with or without the opt-in.
   The build then fails with CS8795 even when the rejecting error is suppressed. This removes the
   all-rejected, invalid-entity and provider facet stubs. It never turns a failing build into a
   compiling one.
3. **INQ039 opt-in covers every rejection.** Any rejected method gets a stub when INQ039 is lowered
   project-wide, so a non-INQ039 rejection runs only after two choices: suppress its error and lower
   INQ039. The provider facet path works this way today. The cost is a widened opt-in. A project
   that already lowers INQ039 and suppresses, for example, INQ095 on a mixed store fails today with
   CS8795 (`WriteEnforcedFilterGeneratorTests.SuppressedUpsertRejectionOnAMultiMethodStoreOmitsTheMethodEntirely`).
   Under this option it compiles, and the method throws at run time.
4. **Make rejecting errors non-configurable.** Tag every rejecting error except INQ039 as
   `NotConfigurable`, and keep emitting stubs for noise reduction. Stubs can never run without the
   INQ039 opt-in. Projects can no longer lower any Inquiry error, including in fixture assemblies,
   and bulk severity settings are ignored for those IDs.
5. **Severity-aware stubs.** Emit a noise-reducing stub only while the generator can prove the
   rejecting error is still an error. Otherwise omit the body. This keeps default output clean, but
   the generator must reproduce every way a severity can change, for every ID. A missed mechanism
   fails open.

### Recommended decision

Choose option 2:

- An Inquiry error that rejects a declaration stays configurable. Suppressing or lowering it never
  produces runnable generated code.
- INQ039 means the declaration is valid but the active dialect cannot run it. Lowering INQ039 below
  error project-wide is the only way to get a `NotSupportedException` stub, and only for those
  methods.
- Any other rejected method has no generated body. The generator still emits the store constructor,
  so CS7036 does not appear, and each rejected method reports CS8795.
- A store is registered in dependency injection whenever its generated source exists. A store with
  a rejected method does not compile, so no build registers it.
- A stub must match its declaration exactly. If the generator cannot reproduce the signature, it
  reports a declaration error and emits no stub for that method.

Option 2 matches the existing documentation and the mixed-store behavior. It changes behavior only
in the fail-closed direction. It costs one CS8795 per rejected method in default builds for
all-rejected and invalid-entity stores, which those stores avoid today. The repair index already
tells readers to start with the first INQ diagnostic and to treat CS8795 as secondary.

Option 3 is the strongest alternative. Choose it if suppressed provider facet errors, such as INQ076,
must keep compiling as stubs. If chosen, the migration notes and #271 must cover projects where
INQ039 is already lowered.

### Consequences for the 1.0 contract

- From 1.0, a generated store method is a throwing stub only when it reports INQ039 and the project
  opted in to INQ039 stubs. No other severity change produces one.
- Two configurations that build today stop building. Both need a changelog entry:
  - A rejecting error is suppressed on an all-rejected or invalid-entity store.
  - A provider facet error, such as INQ076, is suppressed and INQ039 is lowered.
- No configuration that fails today starts to build.
- Diagnostic IDs and default severities do not change, so the `AnalyzerReleases.*.md` files need no
  update.
- Pragmas do not affect generator diagnostics. The repair index should say so, so readers do not
  expect a local opt-out.
- Tests that pin the removed stubs change. These include
  `UpsertRejectionFailsClosedWhenTheDiagnosticIsSuppressed`,
  `SqlServerInvalidCollectionFacetEmitsCompileSafeStubOnlyWithProjectWideOptIn`, and the
  all-rejected and invalid-entity tests that assert no CS8795.

### Follow-up work

- **#266** applies the policy to all-rejected, invalid-entity and extraction-rejected stores. Emit
  the store constructor with no method stubs for each of them, and remove the provider facet stub at
  `StoreProcessor.cs:2226-2234`.
- **#267** makes INQ039 stub signatures match generic, constrained, static and `ref`/`in`/`out`
  declarations. Otherwise it reports a declaration error and emits no stub.
- **#270** emits the store constructor for unsupported token and token-upsert stores instead of
  returning no source. These methods get no stubs.
- **#271** covers each path in the table above in three cases: default severity, the rejecting error
  suppressed, and the rejecting error suppressed with INQ039 lowered.
- **#273** needs no separate stub rule. Its version-guidance error is an ordinary rejecting error. Any
  store method or store it rejects follows the rules above.
- The repair index (`diagnostics.md`) and [Errors and partial outcomes](../articles/error-contracts.md)
  get the final rules when #266 lands.

## Out of scope (by design)

- **Migrations Phase B** (schema diff / `ALTER` / versioning) — delegate to DbUp or FluentMigrator; Inquiry
  generates the initial `CREATE TABLE` DDL only.
- **NoSQL / document engines** (Cosmos DB, MongoDB) — they don't fit a SQL-generating, schema-bound model.
- **JOIN-based or lazy eager loading** — the separate-query model is the recommended high-performance
  pattern; it is not considered a gap.

See the [Roadmap](roadmap.md#explicitly-out-of-scope-for-10) for the same not-planned list in context.
