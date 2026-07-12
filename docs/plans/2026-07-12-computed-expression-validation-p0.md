# Computed-expression validation P0 implementation plan

## Goal

Make `[InquiryColumn(Computed = ...)]` a safe compile-time contract for every bundled provider without pretending Inquiry can fully parse arbitrary provider SQL. Invalid expression *shape* and known provider-incompatible constructs must fail at the property declaration before generated DDL reaches a database. Valid raw provider SQL remains byte-preserved except for an explicitly selected provider override and the existing SQL Server `||` translation.

This is the expression-validation half of the final #175 slice. It does not implement live catalog validation, migrations, or the #72 query manifest.

## Public contract

1. Preserve `InquiryColumnAttribute.Computed` as the required fallback expression.
2. Add repeatable property attribute:

   ```csharp
   [InquiryComputedExpression("mysql", "CONCAT(FirstName, ' ', LastName)")]
   ```

   - constructor arguments: non-empty provider id and non-empty expression;
   - an override is valid only when the same property declares a non-empty `Computed` fallback;
   - add one stable `SqlBuilder.ProviderId` contract, separate from display-only `DialectName`;
   - provider ids are lowercase ASCII matching `[a-z][a-z0-9.-]{0,63}` (`sqlite`, `sqlserver`, `postgresql`, `mysql`, `mariadb`, `oracle` for bundled providers) and match `ProviderId` ordinally;
   - at most one override per provider/property;
   - unknown provider ids are retained for third-party/future providers rather than rejected by a hard-coded built-in list;
   - malformed ids produce a distinct invalid-id `INQ072` reason; a well-formed unknown id is retained silently and simply does not override a different current provider;
   - no provider-specific properties are added to `InquiryColumnAttribute`.
3. Add diagnostic `INQ072` for invalid, ambiguous, or conservatively unsafe schema expressions. It is an error located on the `Computed` named argument or selected override expression argument.

## Internal model and pipeline

1. Add symbol-free equatable `ComputedExpressionOverrideData` and store overrides on `ColumnData`.
2. Preserve the fallback expression and its precise `LocationData` separately from the property location.
3. After dialect arbitration and builder creation, resolve each column's expression once:
   - exact provider override when present;
   - otherwise the fallback expression;
   - retain the selected declared expression for diagnostics, then render it once through the provider seam into a final physical expression;
   - write the final rendered expression into provider-resolved entity records used consistently by materializer/store/schema emission and the later schema manifest. Provider translation must not be repeated independently by the DDL renderer.
4. Run provider resolution/validation after dialect ownership is known but before the provider-resolved `mappedEntities` dictionary is constructed. An invalid selected expression marks that provider-resolved entity unmapped. It must not emit a materializer, registration, schema record, or executable store SQL and must never silently become a writable non-computed column.
5. A store whose root entity, projection source, return shape, or relation dependency requires an invalid entity must receive one actionable declaration diagnostic and the same compile-safe invalid/unsupported stub behavior used by existing store failures; generated partial methods must not disappear and cause CS8795. Cover root CRUD, a store returning the invalid entity, and a relation/eager path.
6. Do not expand `IColumn` for lexer state or diagnostics. The existing `ComputedExpression` member carries only the final provider-rendered physical expression; selected declared raw text remains in internal override/provenance data solely for diagnostics.

## Shared lexer

Create one allocation-conscious lexer used by both validation and SQL Server concatenation translation. It recognizes:

- normal text and identifier/keyword spans;
- single-quoted strings with doubled quote escapes;
- double-quoted identifiers with doubled quote escapes;
- backtick identifiers with doubled backtick escapes;
- SQL Server bracket identifiers with `]]` escapes;
- line comments and block comments;
- parenthesis depth;
- operators and top-level statement separators.

It is a lexer, not a SQL parser. The common validator rejects only facts it can prove:

- null/empty/whitespace expression;
- NUL or disallowed control characters;
- unterminated strings, identifiers, or block comments;
- unmatched parentheses;
- a semicolon outside a literal/comment;
- a `SELECT`/`WITH` subquery token or `OVER` window token outside a literal/comment, because generated-column expressions on all supported baselines must be same-row scalar expressions.

Semicolons/operators/keywords inside strings, quoted identifiers, and comments are preserved and ignored by these checks.

Comment policy is provider-aware and wrapper-safe:

- `/* ... */` is recognized by all bundled providers. SQLite, SQL Server, PostgreSQL, and Oracle recognize `--` unconditionally. MySQL/MariaDB recognize `--` as a line-comment opener only when a following character exists and is whitespace/control; `a--b` and a terminal bare `--` remain ordinary minus tokens, while `a-- b` starts a comment;
- MySQL/MariaDB `#` line comments are recognized only by those provider policies;
- a line comment (`--`, or `#` where supported) that reaches expression EOF is rejected because it would consume the builder-appended closing wrapper syntax; a line comment terminated by a real newline may be retained;
- MySQL executable comments (`/*! ... */`) and MariaDB executable-comment variants are conservatively rejected rather than scanned as inert text;
- a nested block-comment opener is conservatively rejected for a stable cross-provider contract; the lexer does not pretend all providers share nesting semantics;
- provider-specific comment markers that are ordinary operators/tokens elsewhere are not globally classified as comments.

## Provider seams and policies

Add narrow virtual seams to `SqlBuilder`:

- validate the selected computed expression and return zero or more proven failures;
- render the selected expression;
- render the whole computed-column definition;
- expose any provider expression policy needed by a future builder without editing `SchemaEmitter`.

Bundled provider rules:

- SQLite: common validation; preserve raw expression; base `AS (...)` form.
- SQL Server: common validation; reuse the shared lexer to translate only real `||` operators to `+`; quoted/commented pipes remain byte-identical.
- PostgreSQL: common validation; preserve raw expression; typed `GENERATED ALWAYS AS (...) STORED` form. Do not auto-quote identifiers or claim type/function/immutability validation.
- MySQL: common validation; conservatively reject a real `||` operator with `INQ072` because its meaning changes with `PIPES_AS_CONCAT`. This is a mode-independent safety policy, not a claim that the token is invalid SQL. Direct string concatenation to `CONCAT(...)`/a `mysql` override and intentional boolean logic to the unambiguous `OR` keyword.
- MariaDB: the same conservative mode-independent `||` policy, implemented by its own declared policy rather than accidental inheritance of every MySQL semantic rule.
- Oracle: common validation; preserve raw expression; base virtual-column form.

Do not regex-ban arbitrary functions, determinism, aggregates beyond proven window/subquery tokens, identifier existence, types, collation, or cross-column semantics. Those require provider parsing/catalog validation and remain #72/live-DDL concerns.

## Rendering refactor

Route defaults, computed expressions, and checks through distinct builder rendering seams even though only computed validation is required in this PR. The default implementations must remain byte-compatible. This removes raw-expression concatenation from shared schema orchestration and gives new providers one extension point per expression kind.

Do not add provider-specific conditionals to `SchemaEmitter`.

## Tests

### Generator tests on net8.0/net9.0/net10.0

- lexer table tests for every quote/comment form, escapes, Unicode, parentheses, semicolons inside/outside literals, unterminated states, subquery/window tokens, and deterministic diagnostic order;
- adversarial comment tests for EOF `--`, MySQL/MariaDB `a--b`, `a-- b`, terminal bare `--`, EOF `#`, newline-terminated line comments, executable comments, nested block openers, and comment-like tokens inside literals/quoted identifiers;
- precise property/attribute argument locations and one `INQ072` per invalid selected expression;
- override selection/fallback, duplicate override, missing fallback, future provider id retention, source reorder determinism;
- stable lowercase provider-id validation and proof that changing display-only `DialectName` does not change override selection;
- all six provider policies, especially MySQL/MariaDB `||` rejection and override success;
- existing SQL Server adversarial translation test driven through the shared lexer;
- proof that SQL Server's final normalized/rendered expression contains `+` exactly once and is the value consumed by both DDL and schema-manifest construction;
- computed columns remain omitted from insert/update/bulk and selected/materialized;
- invalid computed entities emit no misleading DDL/store SQL; root CRUD, return-shape, and relation-dependent stores remain compile-safe and diagnostic;
- default/check rendering remains byte-identical.

### Live tests on all six providers and all three TFMs

- one portable arithmetic computed column that creates, reads, and recomputes after update;
- one provider-specific string expression selected through the override mechanism where needed;
- identifiers containing spaces/mixed case are explicitly delimited in the raw provider expression;
- known-invalid `||` for MySQL/MariaDB is generator-tested and never reaches a container.

## Verification

- full generator suite, three TFMs;
- focused live computed-column matrix, six providers x three TFMs;
- full Release solution build and test;
- DocFX and package creation;
- public API/package-consumer and NativeAOT smoke checks when the new attribute changes the shipped contract;
- independent adversarial review before PR and Copilot review before merge.

## Non-goals

- parsing or type-checking arbitrary SQL;
- portable property-expression trees;
- validating default/check semantics beyond the new rendering seam;
- catalog normalization or drift comparison;
- query manifests, CLI, MSBuild tasks, or migrations.
