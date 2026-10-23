# Runtime interface guarantees

## 1.0 decision

DefaultInquiry is the supported production implementation of IInquiry. Custom IInquiry and
IInquiryTransaction implementations are supported as test doubles for the behavior an application
chooses to exercise, not as drop-in production runtimes.

Keep the existing interfaces and default implementations for 1.0. Do not split or rename members for
the freeze. Compiling an implementation proves that its required members exist; it does not certify
transactions, batch atomicity, provider support, cancellation, or generated-store compatibility.
No signature migration is required by this decision.

## Application path

Register core services with AddInquiry, one provider, and generated stores, then resolve services
within a scope. Application code normally calls named methods on its stores. Use IInquiry's
FormattableString or InquiryCommand overloads for explicit SQL, buffered/streaming reads, scalar
results, and transaction helpers.

Use IInquiryTransaction for commands tied to a specific transaction and for commit/rollback.
Borrow its Connection and Transaction only for explicit ADO.NET interop; the handle retains ownership.
The built-in transaction enforces its active state and single-operation rule.

For production customization, use the documented connection-factory, interceptor, converter, and
materializer extension points within the built-in runtime. They customize the named concern; they do
not replace the runtime's transaction or resource-ownership machinery. A class merely implementing
IInquiry is not a supported provider-authoring SDK.

## Generated support

The same public interface contains methods that generated stores need to call from the consumer's
assembly. Struct-materializer overloads, immutable generated-command descriptors, generated batch
descriptors, the generator-proven single-row path, and the result-set binding types that map
handwritten-SQL columns by name (`IInquiryResultSetBinder<TEntity, TMaterializer>`,
`InquiryResultShape`, and `InquiryColumnMap`) belong to that support contract. Public
accessibility is required for compilation across assemblies. EditorBrowsable.Never hides some of
these members from completion; it is not a separate runtime capability check.

Application code should not construct generated descriptors or depend on a particular emitted
overload, helper name, or optimization. Let the generator select those calls. Use matching released
runtime/provider packages and rebuild consumers when upgrading; copying generated source is not a
substitute for running the matching generator.

## Grid reader callbacks

InquiryGridReader reads the result sets of one command, in order. QueryMultipleAsync returns it.
Its hidden members ReadRowsAsync, ReadForEachAsync, ReadStreamAsync, and
ReadGeneratedSingleOrDefaultAsync are public only because generated stores in your assembly call
them. They are generated support, not application API. For example, a generated many-to-many eager
load uses ReadRowsAsync to read junction key columns without creating a junction object.

ReadRowsAsync passes the grid's own DbDataReader to its callback, positioned on the current row. The
grid keeps ownership of that reader. A callback must:

- Read the current row only.
- Read each column at most once, in ascending ordinal order. The grid opens the reader with
  SequentialAccess.
- Finish its work before it returns. The callback is synchronous; do not start work that uses the
  reader later.

A callback must not:

- Call Read, NextResult, or their async forms.
- Close or dispose the reader, or dispose the grid.
- Keep the reader, or a stream or text reader obtained from it, after it returns.
- Call another member of the same grid.
- Start another Inquiry operation on the same transaction. While a transacted grid is open, it holds
  the transaction's single in-flight operation, and the new operation throws
  InvalidOperationException.

The grid does not detect a callback that breaks these rules. If the callback calls Read, the grid
skips a row. If it calls NextResult, the grid reads the next result set as if it were the current
one, then skips the set after it. Neither case reports an error. If the callback closes the reader,
the grid's next read fails with the provider's exception.

ReadForEachAsync passes a materialized object, not the reader, so its callback may keep that object.
The same synchronous and no-reentry rules apply.

An exception from a callback, a materializer, or the provider propagates to the caller. The reader
stays inside the current result set. The grid blocks further reads only after an unfinished
ReadStreamAsync. After any other failure, the next read would continue from the middle of the set.
Dispose the grid after an exception instead of reading on. Generated stores do this with await using.

Generated code follows these rules, so the grid adds no per-row guard or wrapper reader. A guard
would add work to every eager-load row without a supported application caller to protect. A new
public raw-row API would need its own decision.

## Default fallbacks are not production guarantees

| Member family | Custom implementation default | Built-in runtime |
|---|---|---|
| FormattableString overloads | Convert to InquiryCommand and dispatch to the command overload. | Executes through the configured pipeline. |
| Generated-command overloads | Convert to InquiryCommand and forward to the corresponding member. | Uses specialized generated execution paths. |
| Generator-proven single row | Falls back to the validating single-or-default query. | Uses the generator's cardinality proof without a second-row probe. |
| QueryMultipleAsync | Throws NotSupportedException. | Owns a grid reader over provider result sets. |
| ExecuteScalarAsync / ExecuteProcedureScalarAsync | Command defaults throw NotSupportedException. | Converts scalar results or reads the selected output parameter. |
| BulkInsertAsync | Throws NotSupportedException, or InvalidOperationException for non-null native options. | Resolves provider bulk support; generated fallback behavior depends on the dialect. |
| List batch / generated bounded batch | Dispatches items or chunks without taking transaction ownership. | Provides atomic non-ambient batch execution and enlists in an ambient transaction. |
| ExecuteInTransactionAsync helpers | Begin, invoke callback, commit, and dispose through the supplied transaction implementation. | Adds the built-in ambient context, state checks, and cleanup behavior. |
| Transaction Connection / Transaction interop | Defaults throw NotSupportedException. | Exposes borrowed ADO.NET handles while active. |

The default options are also limited: ThrowOnConcurrencyConflict is false, and parameter/batch limits
use their interface constants. A test double does not inherit configured InquiryOptions automatically.
Cancellation, materialization, error identity, rollback, disposal, and interception are only as real as
the double's implementation.

A default batch can execute earlier items before a later failure. Neither implementing IInquiry nor
using a generated store makes that fallback atomic. Transaction helpers likewise depend on the
supplied handle actually implementing rollback-on-dispose. Do not use these defaults as production
evidence.

## Testing an application

Prefer a generated store interface or an application-owned data-access interface for business tests.
Use a small IInquiry double when testing generated method dispatch or SQL binding, and implement only
the operations relevant to that test. An unsupported default should fail the test instead of
silently pretending that the database operation succeeded.

Use the built-in runtime against a real provider for transaction, cancellation, batch, stream lifetime,
and database behavior checks. A passing mock-based test cannot establish those guarantees.

See [Errors and partial outcomes](error-contracts.md), [Transactions](features/transactions.md), and
[Registering data libraries](library-registration.md).
