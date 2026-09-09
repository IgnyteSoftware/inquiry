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
| Scalar conversion or materialization | Conversion/provider errors, such as `InvalidCastException`, `FormatException`, or `OverflowException`; user converters can throw their own types. |
| Invalid arguments or configuration | Argument exceptions or `InvalidOperationException`, depending on the API and failed guard. Generated methods do not promise uniform null validation for every input. |
| Concurrency-token update/delete matches no row | Existing false/null result, or `InquiryConcurrencyException` when `ThrowOnConcurrencyConflict` is enabled. |
| Database failure | The provider's exception and diagnostic fields, normally a `DbException` subtype. Preserve these when logging or classifying failures. |
| Cancellation | `OperationCanceledException` (including `TaskCanceledException`) when cancellation is observed. A provider failure after caller cancellation can be wrapped with its original exception as the inner exception, even when its cause was not cancellation. Without caller cancellation, a provider timeout retains its provider identity. |
| Disposed transaction/grid or invalid transaction/grid state | `ObjectDisposedException` or `InvalidOperationException` according to the state guard. |
| Unsupported generated operation | Normally an `INQ039` build error. Deliberately configuring that diagnostic as warning/none emits a `NotSupportedException` stub. |

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

A separately owned grid is different: a read failure and a later `DisposeAsync` failure are separate
operations. C# `await using` can replace a pending read exception with the disposal exception. If both
must be retained, capture the read failure and disposal failure separately in the caller. Do not assume
every exception observed at the end of enumeration describes the database command itself.

## Writes and uncertain outcomes

An exception or cancellation is not proof that nothing changed. A server may complete a write before
the connection fails or the caller observes cancellation. A later SELECT, materializer, interceptor, or
cleanup can fail after the write. Do not blindly retry a non-idempotent operation.

Use explicit transactions for related writes that must commit together. Generated bounded batches on
the built-in runtime own a transaction when there is no ambient transaction; in an ambient transaction
the caller owns commit/rollback. Native bulk-copy guarantees depend on the provider and selected
options. Separate store calls and custom `IInquiry` implementations do not acquire a shared atomic
boundary automatically. A commit/rollback failure can leave the final database outcome uncertain.

Client-generated GUID and audit assignments can mutate input objects before SQL executes. Neither an
execution failure nor database rollback restores those objects. Database-generated identities and
refreshed concurrency tokens belong to the returned materialized entity, not to the original input.

See [CRUD](features/crud.md) for affected rows and generated keys,
[Concurrency](features/concurrency.md) for refresh-after-write,
[Stored procedures](features/stored-procedures.md) for result channels, and
[Transactions](features/transactions.md) for ownership.
