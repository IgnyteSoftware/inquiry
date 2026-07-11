# Issue #172: transaction teardown safety

## Goal

Make ordinary operations, commit, rollback, and disposal share one race-safe lifecycle so no terminal path can touch provider resources while a reader or command owns them. Disposal will fail fast while busy, remain retryable, and clean the transaction and connection exactly once.

## Design

1. Replace `TransactedInquiryRequestPipeline`'s independent in-flight/closed flags with one atomic state: `OpenIdle`, `OpenBusy`, `Terminating`, `Closed`.
2. Ordinary operations CAS `OpenIdle` to `OpenBusy`. A second operation keeps the existing in-flight `InvalidOperationException`; a closing or closed pipeline throws `ObjectDisposedException` before provider access.
3. Commit, rollback, and active disposal CAS `OpenIdle` to `Terminating`. A busy pipeline rejects the terminal attempt without detaching ambient state or poisoning the handle. Accepted terminal ownership closes the pipeline in `finally`.
4. Coordinate each transaction handle with a small locked lifecycle. Concurrent `DisposeAsync` calls share one cached cleanup task. Disposal waits behind an accepted commit/rollback, but never waits for a caller-owned stream.
5. Successful commit/rollback is not repeated by disposal. Failed or cancelled terminal calls permit one best-effort rollback during cleanup.
6. Cleanup swallows rollback-on-dispose failures, preserves the transaction-dispose exception as primary, and always disposes the connection in `finally`. Repeated disposal observes the same result without repeating provider calls.
7. Apply equivalent ownership rules to savepoint handles. Keep ordinary savepoint operations on the outer pipeline gate.
8. Use nested `finally` blocks in streaming and multi-result paths so reader/command disposal failures cannot strand the pipeline in `OpenBusy`.

## Implementation order

1. Add deterministic red tests with controllable fake provider resources for deferred streams, busy disposal, concurrent terminal ownership, cancellation, provider failures, and exactly-once cleanup.
2. Implement the atomic pipeline gate and migrate every operation/terminal entry and exit.
3. Implement root transaction terminal/disposal coordination and ambient-detach timing.
4. Apply the lifecycle to savepoints.
5. Invert the unsafe live SQLite dispose-during-stream expectation and retain a small real-provider smoke matrix.
6. Document concurrency, retry, and post-terminal behavior on the public transaction contract.

## Required tests

- Deferred stream first enumerated after commit, rollback, or disposal fails before command creation.
- Disposal during a held stream/grid fails with the existing in-flight error, changes no terminal/resource counters, and succeeds after the reader is released.
- Commit-vs-rollback, commit-vs-dispose, rollback-vs-dispose, and concurrent-dispose barriers allow one legal owner and exactly-once cleanup.
- Cancelled/failed terminal operations close the logical transaction and are cleaned once; concurrent disposal does not receive the terminal caller's exception.
- Rollback, transaction-dispose, connection-dispose, reader-dispose, and command-dispose failures exercise documented exception precedence and always release the gate.
- Savepoint terminal/disposal races follow the same ownership rules.

## Validation gates

- Focused deterministic transaction state-machine tests on .NET 8, 9, and 10.
- SQLite transaction/ambient integration tests on all supported TFMs.
- Relevant live transaction suites for server providers when available.
- Full runtime tests, Release solution build, package and documentation checks.
- Independent adversarial review before publishing the issue PR.

## Compatibility decision

Disposing a transaction with an active stream or grid now rejects explicitly instead of tearing down resources underneath it. The rejected handle remains active and disposal may be retried after the operation is released. No public signatures change.
