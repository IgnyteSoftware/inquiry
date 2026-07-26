# Issue #171: MySQL-family provider restoration

## Baseline

At `prerelease` `880a16e`, partitioned Docker-required .NET 10 runs show that the prior
180-second MySQL stop was not evidence of a deadlock. A source-identical pre-SQL-Server Release
artifact enumerated MySQL at 245 passed / 6 failed / 0 skipped out of 251 and MariaDB at
251 passed / 3 failed / 0 skipped out of 254. After restoring the damaged package cache and
rebuilding both exact-HEAD binaries, focused confirmation produced three reproducible MySQL
failures and three reproducible MariaDB failures, with zero skips or hangs.

The two MySQL failures and both focused MariaDB failures occur while seeding
`InListBucketingIntegrationTests`, before any IN/NOT IN assertion. Each duplicated
`ProductsDdl` omits four columns mapped by the shared Northwind `Product` entity:
`SupplierID`, `QuantityPerUnit`, `UnitsOnOrder`, and `ReorderLevel`. The generated insert
therefore fails first on `SupplierID`.

The suites are setup-heavy rather than hung. At this SHA, MySQL discovers 251 tests and
creates roughly 229 throwaway databases; MariaDB discovers 254 tests and creates roughly
232. Both projects serialize their live tests through one provider collection. Historical
CI already required up to 3m28s for MySQL. Full-suite commands must use a realistic outer
bound and per-test blame-hang collection instead of treating quiet console output as a hang.

The exact MySQL failures are the two stale Product fixtures and one direct MySqlConnector
cancellation probe with an invalid exception-only oracle. The exact MariaDB failures are the
two Product fixtures and one sub-microsecond audit timestamp mismatch. MySQL's timestamp exact
assertion passed in the confirmation run but previously differed by seven ticks, so it is a
demonstrated timing-dependent precision flake. Both database-default string-key returning tests
passed in the exact run but returned null in the source-identical run. Exact generated-source
inspection proves they still route a VARCHAR UUID key through numeric `LAST_INSERT_ID()` and
`LAST_INSERT_ID(key)`; passing depends on implicit coercion and does not establish correctness.
All 77 MySQL transaction/Northwind/unsigned tests passed; no partition hung or produced a blame
dump.

## Slice A: repair the duplicated Product fixtures

Update the MySQL and MariaDB `ProductsDdl` constants to contain every column written by the
shared Northwind `ProductStore`, with nullability and provider types matching the canonical
MySQL Northwind schema. Do not patch only `SupplierID`; doing so would merely expose the next
missing mapped column.

Correct the test names and comments so they describe the current provider contract:

- MySQL-family IN collections use one JSON array parameter expanded through `JSON_TABLE`;
- NOT IN still uses scalar expansion/padding where applicable;
- the assertions prove correct results across collection cardinalities, not a scalar-bucket
  implementation for both operators.

Do not change generated SQL unless a post-fixture live failure proves a product defect.

Acceptance: both tests pass on MySQL and MariaDB on net8, net9, and net10 with Docker required
and zero skips.

## Slice B: make failed harness setup exception-safe

Both harnesses create a database before executing DDL and building the service provider. If
either later step fails, the current method leaks the throwaway database. Make
`CreateFromDdlAsync` in both providers track successful creation and drop the database before
rethrowing a setup failure.

Requirements:

1. Add a narrow provider-local drop helper shared by failed setup and ordinary disposal.
2. Preserve the original setup exception when cleanup succeeds.
3. If setup and cleanup both fail, preserve both exceptions and identify the database.
4. Expose the generated database name to tests through a test-only callback, following the
   established SQL Server harness pattern.
5. Add one invalid-DDL regression per provider that records the name and proves the database
   no longer exists.
6. Keep ordinary teardown best-effort in this restoration slice. Do not silently broaden a
   failed-setup catch to include failures before database creation.

The existing per-test `ClearAllPools()` calls are a plausible throughput cost, but this PR
must not remove or replace them speculatively. Measure the complete suites after correctness
is green. Provider-specific pool ownership and shared harness consolidation belong to #184
unless a measured change is required to keep the provider leg within the CI budget.

## Slice C: separate AUTO_INCREMENT from database-default keys

The shared MySQL-family builder currently treats every single generated or database-default
key as compatible with `LAST_INSERT_ID`. That is valid only for AUTO_INCREMENT. For a string
key defaulted by `UUID()`, insert-returning omits the key correctly but then queries by numeric
`LAST_INSERT_ID()`. The upsert path is worse: it can coerce a string through
`LAST_INSERT_ID(key)`, select the wrong row, or alter the primary key on a secondary-unique
conflict. MariaDB's native `RETURNING` still calls that shared unsafe upsert builder.

Split the builder into three explicit cases:

1. A single `IsGenerated` AUTO_INCREMENT key retains the current `LAST_INSERT_ID` behavior and
   snapshots unchanged.
2. A single non-auto `UseDatabaseDefault` key never emits `LAST_INSERT_ID`.
3. Ordinary client-supplied keys retain the ordinary insert/upsert path.

For MySQL insert-returning with a non-auto database-default key, the omitted arbitrary default
cannot be recovered safely because MySQL has no DML `RETURNING`. Require a standalone scalar
`DefaultExpression` as the capture recipe. Evaluate it exactly once into an Inquiry-owned
session variable, insert that variable explicitly, and select by the same variable. Use a
quoted variable name containing characters that cannot collide with a C# property-derived
command parameter, after verifying the syntax live on pinned MySQL; do not retain an unchecked
`@_inquiry_genkey` collision. The reproduced model must declare
`DefaultExpression = "(UUID())"`, matching its deployed DDL.
The existing CLR `Guid` convention may fall back to `UUID()` when no expression is supplied,
but a supplied expression wins. Never infer UUID semantics merely from CLR `string`.

If MySQL insert-returning, or a nullable/defaultable upsert-returning whose null branch routes to
insert-returning, has a non-auto database-default key without a usable expression, fail generation
through the existing degradable `NotSupportedException`/INQ039 path with a clear reason and
compile-safe throwing stub. A non-nullable `UseDatabaseDefault` key's upsert-returning path is
explicit-key-only and can safely select by its bound key without `DefaultExpression`; add a generator
case that prevents over-diagnosing it. Non-returning methods and MariaDB native insert-returning must
not acquire a global expression requirement. MySQL composite database-default return shapes must
also degrade rather than emit an unbound or ambiguous predicate.

For explicit non-auto default-key upsert, prepend the key column and parameter but use ordinary
duplicate assignments with no `LAST_INSERT_ID` side effect. When the model declares no secondary
unique constraint, MySQL upsert-returning must append `SELECT ... WHERE key = @key` directly; the only
possible duplicate is the primary key and the actual key is necessarily the attempted key. Reserve
the collision-safe session variable for the omitted/default capture path, including the existing
Guid fallback. Surface exhaustive secondary unique metadata to the store SQL context and degrade
MySQL upsert-returning through INQ039 when a different primary key could win. That metadata must cover
column-level `IsUnique`, named/single-column unique indexes, and normalized composite unique indexes.
It is provider-neutral context data, but only the MySQL default-key returning path may consume it in
this slice; no other provider SQL may change. Add classification tests for column-level and composite
unique forms. Do not capture a string key through `@var := key` inside `ON DUPLICATE KEY UPDATE`: MySQL 8.4
retains that user-variable assignment only as deprecated compatibility syntax and does not offer a
future-safe generic replacement. Querying by mutable non-key values is ambiguous and racy.

Preserve the current contract that insert/insert-returning omit a `UseDatabaseDefault` key, while
upsert is the explicit-or-generated API. Empty string is an explicit key, not a missing key. A schema
with undeclared secondary unique constraints is drift from the model contract and must not be used to
claim safety.

While separating the paths, ensure each duplicate branch assigns the key at most once. The current
key-only AUTO_INCREMENT shape can compose the no-op `key = key` fallback and
`key = LAST_INSERT_ID(key)` into two assignments to the same column. Refactor the assignment helper
so the provider-specific key assignment replaces the empty-SET fallback or appends once after real
mutable assignments; preserve successful AUTO_INCREMENT behavior.

MariaDB must keep native insert/upsert `RETURNING` and must not use session variables or require
`AllowUserVariables` for the non-auto `UseDatabaseDefault` shape. That shape must use the corrected
shared upsert with no `LAST_INSERT_ID`; MariaDB's existing AUTO_INCREMENT behavior remains governed
by case 1 and stays unchanged. A focused
MariaDB 11.4 live test must prove an unambiguous secondary-unique conflict returns the actual updated
row with its unchanged existing primary key. If native upsert-returning does not satisfy that contract,
degrade the declared secondary-unique returning shape through INQ039 unless another atomic shape is
proven; never retain SQL that returns the attempted or wrong row. Primary-key conflict and native
insert-returning remain supported. Update the public `DefaultExpression` documentation to explain its
additional MySQL return-capture role and that the expression must be a standalone scalar matching the
deployed schema.

Generator coverage must prove:

- MySQL string-key insert-returning evaluates the default expression once, inserts/selects by the
  variable, and has no `LAST_INSERT_ID`;
- MySQL explicit upsert-returning handles its insert branch and primary-key conflict by selecting
  directly with `@key`, while a declared secondary-unique shape produces INQ039 rather than deprecated
  or heuristic SQL;
- missing-expression and unsupported composite shapes produce INQ039 plus a throwing stub while
  non-returning and MariaDB-native supported methods still compile;
- the MariaDB non-auto default-key model emits native `RETURNING`, no variable, no
  `LAST_INSERT_ID`, and no user-variable option;
- AUTO_INCREMENT and existing generated-Guid snapshots remain green, including a custom Guid
  default expression overriding the UUID fallback; key-only duplicate SQL assigns the key once;
- a legal `_inquiry_genkey` property/parameter cannot collide with the Inquiry-owned session variable.

Live MySQL and MariaDB tests must cover null/default insert and upsert, explicit-key upsert taking its
insert branch, primary-key conflict, and unchanged stored key. Repeated MySQL default/null calls must
prove the capture variable is initialized on every execution; explicit calls must prove direct
parameter selection. If an entity carrying an explicit value is passed to insert-returning, pin the
existing contract by asserting that value is ignored and replaced by the database/default expression.
MySQL non-returning upsert and MariaDB native upsert-returning must additionally
cover an unambiguous secondary-unique conflict with a different existing primary key; the MySQL
returning equivalent is a generator diagnostic test because that shape is intentionally unsupported.

## Slice D: make audit timestamp assertions respect provider precision

The generator stamps audit timestamps with `DateTime.UtcNow` at 100-nanosecond tick precision;
MySQL and MariaDB store `DATETIME(6)` at microsecond precision. The failing exact assertions differ
by three to seven ticks. Keep production timestamp precision and change the two provider tests to
compare the reconstructed value after truncation to microseconds, following the established
PostgreSQL audit test. Preserve the exact CreatedAt immutability, ModifiedAt advancement, title,
and caller-observable stamping assertions.

## Slice E: make the direct MySqlConnector cancellation probe provider-exact

The existing tests call `DbCommand` directly after obtaining a connection; they do not traverse an
Inquiry operation. MySqlConnector's own integration contract accepts either cancellation or a scalar
`1` when an interrupted standalone `SELECT SLEEP(...)` returns early. MySQL reproduced the latter in
about 1.7 seconds; MariaDB currently throws cancellation.

Rename the tests to identify them as direct provider probes. Use `ExecuteScalarAsync` so a successful
interruption result is observable, require the token to reach the cancelled state, and accept only:

- `OperationCanceledException` for the requested token; or
- the documented scalar interruption result `1`.

Add an independent 10-second wall-clock guard, separate from the 500ms cancellation schedule and
server sleep. On guard expiry, cancel/dispose the command and connection and observe the task within
a second bounded cleanup window so no unobserved fault remains. Reject normal `0` completion,
arbitrary provider errors, and an operation that reaches the full sleep duration. Do not change
production cancellation or claim this closes #156.

## Slice F: preserve issue boundaries

- #156 owns end-to-end Inquiry cancellation-token propagation. The hardened raw MySqlConnector
  probes remain provider tests and do not close it.
- #183 owns bulk transaction, telemetry, and allocation semantics. Existing all-types bulk
  correctness is already green.
- #184 owns source-linked MySQL/MariaDB conformance tests, shared runtime seams, and larger
  provider deduplication. Do not perform that refactor before the green baseline.
- #181 owns generated materializer `SequentialAccess` and wide-row performance coverage.

## Verification

Run provider commands sequentially with no competing test containers. Set
`INQUIRY_REQUIRE_DOCKER=1`, emit TRX artifacts, use `--blame-hang --blame-hang-timeout 2m`,
and give each full provider/TFM leg a practical outer bound below the CI job's 40-minute limit.

1. Invalid-DDL failed-setup cleanup, repaired IN/NOT IN, audit precision, cancellation, and
   default-key returning/conflict tests on net8/net9/net10 for both providers, zero skips.
2. Exact generator SQL/diagnostic tests plus the historical failure classes on net8/net9/net10,
   zero failures/skips.
3. Complete MySQL suite: discovered count must increase from the 251 baseline by the intentional
   new regressions and must not otherwise decrease; zero failures
   and zero skips on net8/net9/net10.
4. Complete MariaDB suite: discovered count must increase from the 254 baseline by the intentional
   new regressions and must not otherwise decrease; zero failures
   and zero skips on net8/net9/net10.
5. Fresh-container net10 repeat for each provider to rule out leaked database/pool state.
6. Record container readiness, per-suite duration, and whether any blame-hang artifact was
   produced. Compare before/after throughput before proposing pool or lifecycle changes.
7. Run generator, core, SQLite, Release build, package smoke, pack, and DocFX gates as mandatory
   production-change release gates.

## Delivery

Use one MySQL-family PR into `prerelease` because the reproduced defect and harness lifecycle
contract are duplicated across the two sibling providers. Require adversarial diff review,
Copilot review, resolution of every actionable thread, and clean local gates before merge.
Update #171 with exact per-TFM counts and timings; keep it open for Oracle and consecutive
full-green release-candidate CI evidence.

Delegate Slice C as one atomic work package to one owner: context uniqueness metadata, shared
builder classification, INQ039 behavior, MySQL/MariaDB SQL snapshots, and default-key live tests
must land together. Slices A, B, D, and E are independent work packages and may be delegated
separately.
