# SQL Server native ROWVERSION plan (#175 slice A)

## Goal

Make `[InquiryConcurrencyToken(DatabaseGenerated = true)] byte[]` a truthful SQL Server
`ROWVERSION` contract. The database must generate and advance the token, generated writes must never
assign it, and stale updates/deletes must continue to report concurrency conflicts.

This is the first of four reviewable #175 slices. Cyclic foreign keys, richer schema primitives, and
provider-scoped computed expressions remain in later #175 slices.

## Contract

- The only valid database-generated token shape is a non-nullable `byte[]` property on SQL Server.
- Its generated column definition is `ROWVERSION NOT NULL`; it is not `VARBINARY(MAX)`.
- SQL Server returning-output table variables use `BINARY(8)`, not `ROWVERSION`, because output
  capture explicitly inserts the generated token into that temporary table.
- `SqlType`, `Length`, `Precision`, `Scale`, `DefaultExpression`, `Computed`, converters, keys,
  database defaults, and nullable token shapes cannot redefine the native rowversion contract.
- Invalid shapes fail at build time at the property location with a dedicated diagnostic.
- Non-SQL Server providers continue to reject database-generated concurrency tokens.
- INSERT/bulk INSERT omit the token. UPDATE/DELETE bind the original token only in the predicate.
- UPDATE-returning materializes the new token. A second write with the stale token affects zero rows
  and follows the configured concurrency-conflict behavior.

## Implementation

1. Add a dedicated diagnostic after the current highest INQ id for an invalid database-generated
   token shape. Validate during entity discovery so an entity is checked even when it has no store
   method. Keep emission safe after reporting the error.
2. Reuse the existing `IColumn.IsDatabaseGeneratedToken` provider seam. In the SQL Server builder,
   render that exact column as `ROWVERSION` and bypass ordinary byte-array type mapping.
3. Add a provider capability hook with a safe unsupported default and a SQL Server override; replace
   dialect-name conditionals for database-generated tokens. Keep `IColumn` and public constructors unchanged.
4. Render rowversion columns as `BINARY(8)` in returning-output table declarations while leaving the
   physical table column as `ROWVERSION`.
5. Add generator tests for exact DDL/output capture, invalid CLR type, nullable byte array, and conflicting metadata.
   Preserve existing insert/update/batch assertions proving the token is omitted from writes.
6. Add a live SQL Server generated-DDL test that inserts, reads, updates, observes a changed eight-byte
   token, proves stale update/delete conflicts, and verifies bulk insertion omits the token. Run it on
   net8.0, net9.0, and net10.0.
7. Update concurrency and schema-DDL documentation to state the exact shape and generated DDL.

## Verification

- Focused generator schema/concurrency tests on all target frameworks.
- Focused SQL Server ROWVERSION live test on all target frameworks.
- SQL Server TVP/bulk regression tests touching generated-column omission.
- Full generator and core runtime suites.
- Solution build, pack, DocFX, and `git diff --check`.
- Independent adversarial review before publishing.
- Ready PR into `prerelease`, Copilot review addressed, then merge.

## Out of scope for this slice

- Cyclic-FK deferral.
- Composite/covering indexes, checks, and FK actions.
- Computed-expression provider scoping and the #72 metadata manifest.
- Bulk-provider type gaps owned by #134 unless a regression is directly caused by ROWVERSION.
