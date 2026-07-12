# Cyclic foreign-key DDL plan (#175 slice B)

## Goal

Make generated baseline DDL executable for schemas containing multi-table foreign-key cycles without
changing the existing single-column foreign-key public API. Defer only the edges that actually take
part in a cycle, keep acyclic dependency ordering, and report an actionable property-located build
error when a provider cannot represent the cycle safely.

This slice does not add composite foreign keys, referential actions, indexes/check constraints, or the
#72 drift manifest. Those consume the normalized constraint model in later #175 slices.

## Contract and resolved choices

- Normalize every emitted foreign key into one internal value-equatable `ForeignKeyConstraintData`
  record containing local schema/table/column, referenced schema/table/column, the local property's
  `LocationData`, a canonical identity, and a deterministic generated constraint name. Do not add
  members to the public `IColumn` interface.
- Run Tarjan's strongly-connected-components algorithm over in-assembly table identities. An edge is
  cyclic exactly when both endpoints belong to the same component of more than one table. Every edge
  inside such an SCC participates in a cycle and is deferred; edges entering or leaving the SCC are
  not. A self-reference remains inline because every supported engine accepts it without a missing-
  table dependency.
- SQLite keeps cyclic constraints inline. SQLite accepts forward references in `CREATE TABLE` and has
  no supported `ALTER TABLE ... ADD CONSTRAINT` form.
- SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle omit cyclic edges from `CREATE TABLE` and append
  `ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY ... REFERENCES ...` after all tables exist.
- Add an explicit provider strategy seam, for example `CyclicForeignKeyStrategy` with
  `ReportDiagnostic`, `Inline`, and `AlterTable`. The safe base default is `ReportDiagnostic`; all six
  bundled builders override explicitly. The fallback reports new error `INQ069` at each affected
  local FK property and does not emit a knowingly invalid constraint.
- Generated names are always hash-suffixed and no more than 63 UTF-8 bytes. Build the hash from a
  length-delimited canonical identity `(local schema, table, column, referenced schema, table,
  column)`, use SHA-256 with a stable lowercase-hex suffix, and truncate the readable `FK_...` prefix
  only at a complete UTF-8 scalar boundary. Resolve the rare duplicate canonical/name collision
  deterministically and diagnose duplicate physical FK declarations rather than relying on source
  order.
- Emit phases in this order: provider artifacts, all `CREATE TABLE` statements, all deferred FK
  `ALTER TABLE` statements, then all `CREATE INDEX` statements. This keeps every referenced table in
  place before constraints and every table/constraint in place before indexes.
- Do not add existence guards for deferred constraints. `InquiryGeneratedSchema.Ddl` is baseline/
  bootstrap DDL, and the existing SQL Server/MySQL/MariaDB/Oracle index statements and Oracle tables
  already make the full script run-once. PostgreSQL's and SQLite's partial `IF NOT EXISTS` support does
  not make the whole script replayable. Document deferred ALTER statements as run-once instead of
  adding catalog queries, procedural blocks, or provider-specific pseudo-idempotency.

## Model and provider seams

1. Add `ForeignKeyConstraintData` under the internal generator models. Populate it from resolved
   `ColumnData` after inherited FK lengths are known. Preserve `ColumnData.Location`, introduced in
   slice A, as the diagnostic source.
2. Build a table-identity lookup using the same schema/table comparison rules as dependency ordering.
   Cross-assembly/unmapped references stay inline and do not create graph vertices; the generator
   cannot prove them cyclic.
3. Replace the current DFS ordering/cycle fallback with one graph analysis result containing:
   - Tarjan component id/size for every local table;
   - the exact deferred-edge set;
   - a deterministic topological order of the SCC condensation graph, with source identity as the
     tie-breaker.
   This preserves referenced-before-dependent ordering for acyclic edges and stable output for cycles.
4. Keep the existing public `SqlBuildContext` constructor and `IColumn` surface binary compatible.
   Add an internal schema-emission construction path/property carrying the exact inline FK set (or
   deferred local-column identities), which the base `BuildCreateTableSql` consults. Query/store
   contexts remain unchanged.
5. Add an additive virtual provider capability/strategy property to `SqlBuilder`, defaulting to
   diagnostic. Add a default standard-SQL deferred-FK renderer that receives already-normalized names
   and identifiers and quotes exclusively through `QuoteIdentifier`/`QuoteTable`. Provider overrides
   should be necessary only if live syntax proves a difference.
6. Keep self-references inline on all providers. For a provider using the diagnostic fallback, report
   one INQ069 per exact multi-table cyclic edge, suppress that edge, and continue emitting safe table
   DDL so generated C# remains valid after the error.

## Diagnostics

Add `INQ069`, error and enabled by default:

> Entity '{0}' foreign-key property '{1}' participates in a schema cycle, but provider '{2}' cannot
> add the constraint after table creation. Break the cycle, disable generated foreign keys for the
> table, or use a provider that supports deferred constraint creation.

Use the local FK property's `LocationData`. Do not also emit a table/store-level diagnostic for the
same edge. Invalid or duplicate FK declarations discovered while normalization is built should use a
single property-located error and be excluded from both inline and deferred output.

## Tests

### Generator tests

- Two-table and three-table cycles: assert only intra-SCC edges are deferred and entering/leaving
  acyclic edges remain inline.
- A graph with a cycle plus an unrelated chain: assert deterministic SCC-condensation order and exact
  ALTER placement after the final table but before the first index.
- SQLite: all cycle edges remain inline and no ALTER appears.
- SQL Server/PostgreSQL/MySQL/MariaDB/Oracle: cycle edges are absent from table bodies and appear once
  as named ALTER constraints with provider quoting.
- Self-reference remains inline on all six providers.
- `GenerateForeignKeys = false` contributes no graph edge, diagnostic, inline constraint, or ALTER.
- Cross-schema same-named tables use distinct graph identities and constraint names.
- Long ASCII and multibyte identifiers produce stable names at most 63 UTF-8 bytes, with the expected
  hash suffix; reordered source declarations produce byte-identical names and DDL.
- A test-only builder using the base diagnostic strategy reports exactly one property-located INQ069
  per cyclic edge and emits no invalid constraint.
- Existing acyclic and schema-qualified FK snapshots remain byte-stable except where deterministic
  explicit constraint naming is intentionally introduced. If inline acyclic constraints remain
  unnamed, state and test that compatibility choice explicitly.

### Live DDL matrix

Add the same minimal two-table cycle and self-reference fixture to every provider integration project.
Execute `InquiryGeneratedSchema.Ddl`, insert mutually referencing rows using a nullable-first/update
sequence, and verify both FK directions reject an invalid reference.

- SQLite validates the inline-cycle path with foreign keys enabled.
- SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle validate deferred ALTER syntax and generated
  names through catalog/introspection queries.
- Run each focused live fixture on net8.0, net9.0, and net10.0. Keep provider-version assumptions in
  the existing container fixtures; do not weaken tests to snapshots when a live engine is available.

## Exact implementation areas

- `src/Inquiry.Generators.Shared/Models/ForeignKeyConstraintData.cs` (new)
- `src/Inquiry.Generators.Shared/SchemaEmitter.cs`
- `src/Inquiry.Generators.Shared/Abstractions/SqlBuildContext.cs`
- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`
- `src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs`
- all six provider `*SqlBuilder.cs` files for explicit cycle strategies
- focused generator schema tests and all six provider generated-DDL integration fixtures
- schema-DDL documentation describing cycle handling, deterministic names, and run-once ALTER policy

## Verification

1. Focused cycle/name/diagnostic generator tests, then the full generator suite on net8/net9/net10.
2. Focused generated-DDL live tests for SQLite, SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle on
   net8/net9/net10.
3. Existing schema DDL, schema-index, FK-length, Northwind, and provider generated-DDL regressions.
4. Full runtime tests, release solution build, pack, DocFX, and `git diff --check`.
5. Final source audit: no deferred edge remains inline, no acyclic edge is unnecessarily deferred, no
   SQLite ALTER FK exists, every deferred name is <=63 UTF-8 bytes, and no duplicate INQ069 is emitted.
6. Independent adversarial review before publishing the slice PR into `prerelease`; wait for and
   resolve Copilot review, rerun affected local tests, then merge.
