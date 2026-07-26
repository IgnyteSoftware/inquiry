# Issue #171: PostgreSQL provider restoration

## Baseline

At `prerelease` `313eda1`, a Docker-required PostgreSQL net10 run has 27 failures and 223 passes.
Focused reproduction assigns every failure to one of five independent root causes:

- 17 transaction/ambient tests use handwritten unquoted PascalCase Northwind identifiers;
- 2 in-list tests create an incomplete `Products` fixture before inserting the full mapped entity;
- 1 generated-DDL test supplies an unquoted PostgreSQL computed expression for quoted columns;
- 3 default-value tests declare a `Guid` key as PostgreSQL `TEXT` rather than `UUID`;
- 4 audit tests bind UTC `DateTime` values to `timestamp without time zone`, which Npgsql rejects.

The first four are fixture/declaration defects. The DateTime cluster is a provider binding defect affecting
all UTC/local `DateTime` values, not only auditing.

## Slice A: correct PostgreSQL fixtures without weakening product assertions

1. Add a narrow test-only `PostgreSqlNorthwindSql` helper containing literal, double-quoted Northwind
   identifier templates for customer insert, update, count, select-all, and select-by-key. Only values remain
   interpolated/parameterized. Replace the six handwritten SQL sites in transaction and ambient tests.
2. Expand the in-list test's local `Products` table with the four mapped nullable columns currently omitted:
   `SupplierID`, `QuantityPerUnit`, `UnitsOnOrder`, and `ReorderLevel`. Do not add unrelated foreign keys.
3. Add a `postgresql` override to `ComputedPerson.FullName` using
   `"FirstName" || ' ' || "LastName"`, leaving the fallback and MySQL-family overrides unchanged. Strengthen
   generator coverage for the exact PostgreSQL expression.
4. Change only `TDefaultedItem."Key"` from `TEXT` to `UUID`; retain the separate text-key fixture that
   intentionally uses `gen_random_uuid()::text`.

These are test/declaration corrections. Do not change production identifier quoting, rewrite raw user SQL, or
lowercase the canonical Northwind schema.

## Slice B: normalize PostgreSQL DateTime parameter values

1. Override `PostgreSqlSqlBuilder.BuildParameterValueExpression` for effective provider values whose special
   type is `DateTime`. Emit `DateTime.SpecifyKind(value, DateTimeKind.Unspecified)` so Npgsql can bind the
   unchanged ticks to PostgreSQL `TIMESTAMP` (`timestamp without time zone`). This applies uniformly to direct,
   nullable, converter-provider, audit, and bulk accessor paths. Do not mutate entity properties.
2. Apply the same transformation in `BuildCollectionElementExpression`, preserving the existing unsigned
   transformations and nullable projection behavior.
3. Leave `DateTimeOffset` unchanged: it remains mapped to `TIMESTAMPTZ` and represents an instant normalized
   to UTC. PostgreSQL does not persist the original offset, and Npgsql requires a zero-offset value. Do not
   silently normalize or strip a non-zero offset in this slice.
4. Add generator tests proving direct, nullable, converter-provider, bulk, and collection DateTime expressions
   use the bridge, while DateTimeOffset does not.
5. Add live PostgreSQL scalar coverage for fixed-tick UTC, Local, and Unspecified DateTime values through
   ordinary generated binding. Verify exact ticks and materialized `Kind == Unspecified` for all three. Add a
   live `IN`/`ANY` collection case containing UTC and Local DateTimes so the separate collection projection is
   proven against Npgsql, including a nullable or converter-backed collection when that operation is supported.
   Existing audit tests prove generated UTC stamps and update/batch paths.
6. Add a live zero-offset DateTimeOffset round trip and a focused non-zero-offset contract test that preserves
   Npgsql's rejection; Inquiry must not silently change the represented instant or offset.
7. Document the provider contract: PostgreSQL `DateTime` stores wall-clock ticks in `TIMESTAMP` and does not
   persist `Kind`. Use zero-offset `DateTimeOffset` for instant semantics; PostgreSQL does not persist the
   original offset, and non-zero offsets must be explicitly normalized by the application before binding.

## Verification

Run Docker-required focused classes on .NET 8, 9, and 10 with zero skips:

- `TransactionIntegrationTests` + `AmbientTransactionIntegrationTests`: 37/37 per TFM;
- `InListBucketingIntegrationTests`: 2/2 per TFM;
- `GeneratedDdlIntegrationTests`: 2/2 per TFM;
- `DefaultValueIntegrationTests`: 5/5 per TFM;
- `AuditTimestampIntegrationTests` plus new DateTime binding tests: all green per TFM.

Then run the complete PostgreSQL suite on all three TFMs with Docker required. The net10 result must move from
27 failures to zero, with no muted/weakened tests. Run full generator/core/SQLite regression suites, Release
build, package smoke, pack, and DocFX in proportion to the changed generator/provider/documentation surface.

## Boundaries

- This PR restores PostgreSQL only; #171 remains open until all five server providers and required CI runs are
  green.
- Do not absorb SQL Server TVP work (#69), bulk performance architecture (#183), or release workflow gates
  (#89).
