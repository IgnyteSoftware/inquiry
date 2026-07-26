# Issue #171: SQL Server provider restoration

## Baseline

At `prerelease` `cdc658b`, an isolated Docker-required .NET 10 run of the 12 historically failing
SQL Server classes completed in 25.67 seconds: 32 passed, 4 failed, and 0 skipped. The historical TVP,
materialization, generated-DDL, enum, bulk, and batch failures are already fixed by #134, #174, #175,
and #199. Current work must not reopen or absorb #69.

The four remaining failures have three independent roots:

- two view tests send `CREATE TABLE` and `CREATE VIEW` in one SQL Server batch;
- key-only generated-identity upsert-returning returns no row when an explicit key already exists;
- the direct SqlClient cancellation probe expects only `OperationCanceledException`, while current
  Microsoft.Data.SqlClient can report cancellation as `SqlException`.

## Slice A: correct the SQL Server view fixture

Change `FeatureSchema.ViewEntitySqlServerDdl` so the static `CREATE VIEW` executes in its own dynamic batch
(for example, `EXEC(N'CREATE VIEW ...')`) after the table statement. Do not add a generic batch parser or
weaken the aggregation and predicate assertions. The dynamic SQL is a fixed test constant and contains no
runtime values.

Also make `SqlServerTestHarness.CreateFromDdlAsync` exception-safe. Once it creates the throwaway database,
any artifact, DDL, or service-provider setup failure must drop that database before propagating the original
failure. Share a narrow drop helper with `DisposeAsync` if useful. If cleanup itself fails, preserve both the
original setup exception and cleanup context; do not replace or silently swallow the setup failure. Add a
deliberately invalid-DDL regression that records the generated database name and proves it no longer exists.
Ordinary best-effort teardown behavior may remain unchanged.

Acceptance: both `ViewEntityIntegrationTests` pass on .NET 8, 9, and 10 with Docker required and zero skips.

## Slice B: return the existing row for key-only upsert conflicts

For the returning branch when `SetClauses` is empty:

1. Establish statement-local atomicity without blindly rolling back a caller-owned transaction: start and own
   a transaction only when none exists; otherwise create a uniquely named savepoint. In `CATCH`, roll back the
   owned transaction, or roll back only to the savepoint when `XACT_STATE() = 1`; preserve an uncommittable
   caller transaction for its owner to observe and handle.
2. Insert the matching existing row into `@_out` under `UPDLOCK, SERIALIZABLE`, with explicit target and source
   columns in identical entity projection order.
3. Check `@@ROWCOUNT` immediately. Only when no row was captured, enable `IDENTITY_INSERT` (for identity keys),
   insert the explicit key, capture `INSERTED` into `@_out`, and disable `IDENTITY_INSERT`.
4. Track whether `IDENTITY_INSERT ON` succeeded. On failure in a still-live session, attempt
   `IDENTITY_INSERT OFF` in nested cleanup before rethrowing the original SQL error. Cleanup must not replace
   that original error. The generated-SQL guarantee is limited to a live session where cleanup SQL can
   execute; an actually broken physical connection is discarded by SqlClient's connection-state/pooling
   behavior rather than by generated T-SQL. Never enable identity insert for an existing-row conflict.
5. Commit only a transaction owned by this statement and use the existing trailing select.

This must preserve the atomic insert-if-absent contract, trigger-safe `OUTPUT INTO`, generated-null-key insert,
explicit-key insert, explicit-key conflict/no-op, projection order, and the non-returning affected-row contract.
Do not emit an empty `UPDATE SET`, perform a second unlocked post-commit lookup, or change the ordinary upsert
path with writable columns. Apply the same failure-safe `IDENTITY_INSERT` discipline to the non-returning
empty-SET explicit-insert branch if it shares the unsafe sequence; its successful behavior remains unchanged.

Add exact SQL-generator coverage for all four relevant shapes:

- key-only generated identity, non-returning and returning runtime branches;
- generated-key empty-SET multi-column projection (for example, key plus created-audit column), including
  explicit target/source column order;
- ordinary generated-key upsert with writable columns remains update-first.

Null-key and explicit-key behavior are runtime branches of one generated statement, not separate generator
shapes. Assert exact statement ordering: transaction/savepoint setup; lock-and-capture; immediate
`IF @@ROWCOUNT = 0`; guarded identity insert; owned commit; trailing output select; then failure cleanup.

Live tests must prove:

- explicit-key insert and conflict both return the row, generated-null-key insert returns the assigned key,
  row count is unchanged by conflict, and no duplicate is created;
- concurrent same-key `UpsertReturningAsync` calls from separate connections all return that key, produce no
  duplicate/primary-key exception, and leave exactly one row;
- a multi-column empty-SET entity returns every projected value in correct order on conflict;
- an injected explicit-insert constraint failure on a live session preserves the original error, confirms
  `IDENTITY_INSERT` is off, and leaves no statement-owned transaction open;
- inside a caller-owned transaction, a committable failure (`XACT_STATE() = 1`) rolls back only to the
  operation savepoint and permits a following command. A separately induced doomed transaction
  (`XACT_STATE() = -1`) must not attempt savepoint rollback or commit; prove the owner observes the doomed
  state and can perform the required full rollback. Do not claim the committable constraint case proves the
  doomed branch.

## Slice C: make the direct SqlClient cancellation probe provider-exact

Rename the test to identify it as a direct SqlClient cancellation probe. It calls
`DbCommand.ExecuteNonQueryAsync` directly, outside Inquiry's command pipeline, so it cannot require Inquiry to
translate provider exceptions. Preserve the cancellation-propagation proof by asserting:

- the token source actually reached the cancelled state;
- an independent 10-second `Task.WhenAny` wall-clock guard, with token cancellation scheduled at 500ms,
  cancels/disposes the command and connection before failing if execution does not finish; this is separate
  from the 30-second `WAITFOR` and makes a propagation regression deterministic;
- the outcome is either `OperationCanceledException` or Microsoft.Data.SqlClient's cancellation-specific
  `SqlException`, using the observed structured `SqlError` number/class/state tuple for the repository's pinned
  Microsoft.Data.SqlClient 7.0.1 across net8/net9/net10. Isolate a narrowly matched cancellation-message
  fallback only if the provider supplies no stable tuple;
- arbitrary `DbException`/`SqlException` failures are rejected.

Do not add production exception normalization in this fixture-only slice. A public Inquiry exception contract,
if desired, requires a separate pipeline-level test and design decision.

## Slice D: make advertised SQL Server full-text coverage release-gating

The complete suite currently reports 290 passed and 3 skipped per TFM because Microsoft's pinned SQL Server
container does not install the optional Full-Text Search package. Inquiry advertises SQL Server full-text as
supported, so required provider runs may not convert that missing test dependency—or arbitrary catalog/index
setup failures—into skips.

1. Add a small checked-in SQL Server 2022 CU14 Ubuntu 22.04 test Dockerfile following Microsoft's documented
   custom-container pattern. Pin the Microsoft base by both `2022-CU14-ubuntu-22.04` tag and resolved digest.
   Install exact `mssql-server-fts=16.0.4135.4-3` with `--no-install-recommends`; use Microsoft's signed
   repository/key setup without `curl | bash`, clean apt lists, and fail the build unless installed engine and
   FTS package versions are the intended compatible pair. End with `USER mssql`.
2. Add `INQUIRY_SQLSERVER_IMAGE` as a fixture-only image override. The local default remains the official
   pinned image, while release-required runs select the built FTS tag.
3. In `FullTextSearchIntegrationTests`, capability absence may skip only for an ordinary local run. When
   `INQUIRY_REQUIRE_DOCKER=1`, `IsFullTextInstalled != 1` is a hard test failure. Remove the broad catch that
   turns catalog/index SQL defects into skips; setup exceptions must fail normally.
4. In both PR and scheduled provider workflows, after checkout, run build and preflight steps only when
   `matrix.provider == 'SqlServer'`, using one deterministic local tag. Preflight must fail if the image's
   configured/default runtime user resolves to UID 0. Pass `INQUIRY_SQLSERVER_IMAGE` only to the SQL Server
   test step; it stays unset/blank for other providers, whose fixtures remain unchanged. Keep job-level
   `INQUIRY_REQUIRE_DOCKER=1`, the existing project/TFM test command, and the 40-minute budget. Do not start a
   second SQL Server service container/job solely for FTS.
5. Make the SQL Server fixture log the selected image and verify `FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')
   = 1` immediately after startup in required mode, before the suite can claim success. An explicitly supplied
   image that is missing, cannot start, runs as root, or lacks FTS must fail; never silently fall back to the
   official default after override failure.
6. Document the local opt-in build/tag/environment command near the SQL Server test or contributing guidance.

Acceptance: `FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1`; all three FTS tests pass; deliberately invalid
catalog/index setup fails rather than skips; required mode without the capability fails explicitly; complete
SQL Server net8/net9/net10 suites discover and report their new exact total with 0 failed and 0 skipped using
the FTS image. Freeze that total only after the new policy/setup regressions are included.

Additional test requirements:

- `WaitForPopulationAsync` uses monotonic elapsed time and fails with the last observed status after its bound;
  it must not silently return, because an unpopulated index can make the no-match test falsely pass.
- Required-versus-local capability policy is a small pure helper tested with explicit booleans; tests must not
  mutate `INQUIRY_REQUIRE_DOCKER` process-wide.
- Invalid catalog/index coverage exercises the same setup helper and preserves the original SQL exception; it
  is not an unrelated synthetic invalid command.
- Record image-build, container-readiness, and full-suite durations. The cold build plus suite must retain
  practical margin under 40 minutes. Use BuildKit/GitHub Actions caching if repeated exact-package download is
  materially variable; never float a newly published FTS package in release-gating CI.

## Verification

Run Docker-required tests with zero skips:

1. The original four affected tests plus new same-key concurrency, multi-column projection,
   identity/transaction-failure cleanup, and harness failed-setup cleanup on net8/net9/net10.
2. Full-text capability/setup tests, including required-mode failure and invalid-setup non-skip behavior.
3. The exact 12-class historical filter on net8/net9/net10, with a two-minute blame-hang guard.
4. The complete FTS-image SQL Server suite on net8/net9/net10: report the post-change discovered total per
   TFM with zero failures/skips.
5. Repeat complete net10 against a fresh FTS-capable SQL Server container to rule out TVP artifact/cache
   leakage.
6. Full generator/core/SQLite regression suites, Release build, package smoke, pack, and DocFX because the
   production change affects generated SQL and provider packaging.

Run provider commands sequentially with no competing provider test containers, emit TRX artifacts, retain
blame-hang diagnostics, and impose a practical outer bound below the CI leg's 40-minute limit.

## Boundaries

- This PR restores only the SQL Server leg of #171; #171 remains open for MySQL, MariaDB, Oracle, CI, and the
  required consecutive full-green release runs.
- Do not change #69 TVP metadata/streaming, #188 stored-procedure metadata, or #87 benchmarks.
- Do not claim the prior full-suite timeout as a product hang; it occurred under concurrent provider-container
  contention and produced no finalized result.
