# Errors and partial outcomes

These contracts describe the built-in runtime and generated stores. Custom `IInquiry` implementations,
interceptors, converters, and materializers can introduce their own exceptions. Inquiry does not
replace every failure with a universal exception.

## Results and error types

| Situation | Result or failure |
|---|---|
| Validating `QuerySingleOrDefaultAsync` or grid `ReadSingleOrDefaultAsync` | Zero rows returns null; one returns the entity; a second row throws `InvalidOperationException`. It is not a first-row API. |
| Generator-proven key lookup or top-one query | Returns the matching entity or null. The optimized generated path relies on the generated query's cardinality rather than probing a second row. |
| Buffered query with no rows | Empty list; no exception merely because no rows matched. |
| Scalar SQL NULL / DBNull | `default(T)`, including zero for non-nullable numeric types. Choose a nullable result to preserve NULL. |
| Scalar conversion or materialization | Conversion/provider errors, such as `InvalidCastException`, `FormatException`, or `OverflowException`; user converters can throw their own types. Generated store queries throw these unchanged. Handwritten SQL and stored-procedure rows wrap them in `InvalidOperationException`, which names the property and the column and keeps the original as `InnerException`. |
| Handwritten SQL or procedure result columns do not match the result type | `InvalidOperationException` before the first row, naming the type, the property, and the available columns. A missing column and two columns that match one property both fail this way. See [ad-hoc DTOs](features/ad-hoc-dtos.md#column-mapping). |
| Invalid arguments or configuration | Argument exceptions or `InvalidOperationException`, depending on the API and failed guard. Generated methods do not promise uniform null validation for every input. |
| Concurrency-token update/delete matches no row | Existing false/null result, or `InquiryConcurrencyException` when `ThrowOnConcurrencyConflict` is enabled. |
| Database failure | The provider's exception and diagnostic fields, normally a `DbException` subtype. Preserve these when logging or classifying failures. |
| Cancellation | `OperationCanceledException` (including `TaskCanceledException`) when cancellation is observed. A provider failure after caller cancellation can be wrapped with its original exception as the inner exception, even when its cause was not cancellation. Without caller cancellation, a provider timeout retains its provider identity. Every `InquiryGridReader` read reports cancellation the same way, with the caller's token. A failure from a materializer or a row callback is not wrapped. If a grid read is cancelled and the grid's cleanup then also fails, the `OperationCanceledException` arrives as the first inner exception of an `AggregateException` (see [Cleanup failures](#cleanup-failures)). |
| Disposed transaction/grid or invalid transaction/grid state | `ObjectDisposedException` or `InvalidOperationException` according to the state guard. |
| Unsupported generated operation | Normally an `INQ039` build error. Deliberately configuring that diagnostic as warning/none project-wide emits a stub that throws `NotSupportedException` synchronously. Only methods that report `INQ039` get a stub. |
| Rejected store declaration | Any other Inquiry error that rejects a store method or a whole store, such as an invalid entity or an unsupported method signature, fails the build. Suppressing or lowering it does not produce a stub or a working method: the rejected method has no generated implementation and reports CS8795. See [severity and suppression](diagnostics.md#severity-and-suppression). |

## When failures appear

Catch around both invocation and awaiting. Synchronous wrappers and generated argument preparation can
throw before returning a task. Async execution, row conversion, interceptors, and resource cleanup can
fault the task. A successfully executed command can still be followed by a conversion or cleanup error.

Streaming defers database work until enumeration. Catch around the whole `await foreach`, not just
the call that obtains `IAsyncEnumerable<T>`. A stream can yield rows and then fail on a later read,
materialization, cancellation, or cleanup. Already yielded objects and caller side effects remain.
Do not return a live stream beyond its service/transaction scope. Dispose its enumerator even when
stopping early; `await foreach` does this automatically.

A grid owns its reader and command and must be disposed with `await using`. Read result sets once,
in order. Finish a grid stream before reading another result set: abandoning the stream makes
subsequent reads invalid. A disposed grid rejects reads, and reading beyond the last set is an error.

## Cleanup failures

Inquiry attempts the remaining cleanup steps even if one fails. A single cleanup failure is rethrown;
multiple failures produce `AggregateException`. On paths that capture both execution and cleanup
failures, the aggregate contains the original execution exception first, followed by cleanup failures.
Inspect inner exceptions to retain provider error identity.

A grid keeps the first read failure. If a later `DisposeAsync` then fails, it throws
`AggregateException` with the read failure first, followed by the cleanup failures, even when only one
cleanup step failed. The read failure includes a cancellation: the `OperationCanceledException`, with the
caller's token, is the first inner exception. This applies to every grid, including those that generated
eager-load and `InquiryPagedResult<T>` methods dispose with `await using`, so a cleanup failure no longer
replaces the read failure. A caller that catches `OperationCanceledException` around such a method must
also inspect an `AggregateException` when cleanup can fail. Do not assume every exception observed at the
end of enumeration describes the database command itself.

## Writes and uncertain outcomes

An exception or cancellation is not proof that nothing changed. A server may complete a write before
the connection fails or the caller observes cancellation. A later SELECT, materializer, interceptor, or
cleanup can fail after the write. Do not blindly retry a non-idempotent operation.

Use explicit transactions for related writes that must commit together. Generated bounded batches on
the built-in runtime own a transaction when there is no ambient transaction; in an ambient transaction
the caller owns commit/rollback. Native bulk-copy guarantees depend on the provider and selected
options; see [Partial writes and retries](features/bulk-insert.md#partial-writes-and-retries). Separate store calls and custom `IInquiry` implementations do not acquire a shared atomic
boundary automatically. A commit/rollback failure can leave the final database outcome uncertain.

Client-generated GUID and audit assignments can mutate input objects before SQL executes. Neither an
execution failure nor database rollback restores those objects. Database-generated identities and
refreshed concurrency tokens belong to the returned materialized entity, not to the original input.

See [CRUD](features/crud.md) for affected rows and generated keys,
[Concurrency](features/concurrency.md) for refresh-after-write,
[Stored procedures](features/stored-procedures.md) for result channels, and
[Transactions](features/transactions.md) for ownership.
