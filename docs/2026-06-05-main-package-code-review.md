# Inquiry main package code review - 2026-06-05

## Scope

Reviewed the main runtime NuGet package surface under `src/Inquiry`, excluding `bin/` and `obj/`. I also inspected generator code where a core-package attribute or API directly determines emitted SQL or registration behavior.

## Implementation progress

Started on the P1/P2 release blockers in branch `codex-main-package-findings`.

- Fixed soft-delete predicate precedence for `OR` predicates by grouping existing predicate bodies before global `AND` filters are appended. Added a generator regression for `OR + soft delete`.
- Fixed ambient transaction straggler escapes by distinguishing fresh post-transaction work from async work that captured a transaction slot before close. Fresh store calls after the transaction still use the default pipeline; captured late work now throws `ObjectDisposedException`.
- Fixed root transaction commit/rollback serialization by sharing the transacted pipeline in-flight guard with root close operations. Added SQLite regressions for commit/rollback while a streaming reader is active.
- Fixed batch mutation/concurrency-token bypass by rejecting `[InquiryUpdateAll]` and `[InquiryDeleteAll]` on token entities with `INQ022`.
- Added a configurable parameter cap (`InquiryOptions.MaxParametersPerCommand`, default 2000) and wired it through `Compare.In`, batch delete, batch insert, and batch update expansion.
- Fixed repeated `AddInquiry(...)` option configuration so later calls compose into the single registered `InquiryOptions` instance instead of being silently ignored.
- Added `InquirySql.Sql(FormattableString)` as an explicit safe ad-hoc SQL factory that parameterizes interpolation holes into an `InquiryCommand`.
- Replaced default AppDomain-wide generated-service discovery with explicit generated registration: `AddInquiry()` is core-only, the generator emits `AddInquiryGeneratedStores()`, and the assembly overload scans only assemblies the caller supplies.
- Updated transaction docs to describe the new captured-slot behavior.
- Updated security and batch-operation docs for `InquirySql.Sql(...)` and `InquiryOptions.MaxParametersPerCommand`.

Verification run:

- `dotnet test tests\Inquiry.Generators.Tests\Inquiry.Generators.Tests.csproj` - passed: 184 tests on net8.0, net9.0, and net10.0.
- `dotnet test tests\Inquiry.Sqlite.Tests\Inquiry.Sqlite.Tests.csproj` - passed: 156 tests on net8.0, net9.0, and net10.0.
- `dotnet test tests\Inquiry.Tests\Inquiry.Tests.csproj` - passed: 102 tests on net8.0, net9.0, and net10.0.
- `dotnet build samples\Inquiry.Sample\Inquiry.Sample.csproj` - passed.
- `dotnet build tests\Inquiry.SqlServer.Tests\Inquiry.SqlServer.Tests.csproj` - passed with existing benchmark nullability warnings.
- `dotnet build tests\Inquiry.PostgreSql.Tests\Inquiry.PostgreSql.Tests.csproj` - passed.
- `dotnet build tests\Inquiry.MySql.Tests\Inquiry.MySql.Tests.csproj` - passed.
- `dotnet build tests\Inquiry.Oracle.Tests\Inquiry.Oracle.Tests.csproj` - passed.
- `git diff --check` - passed with only existing line-ending normalization warnings from Git.
- `dotnet test Inquiry.slnx` - previously attempted, but the command timed out after five minutes before returning output. The solution includes Docker-backed provider suites, so run those separately when Docker availability is known.

Security review used the Codex Security phase model:

1. Threat model: runtime package assets are SQL text integrity, parameter binding, transaction atomicity, generated-store registration, soft-delete/concurrency invariants, and safe startup configuration.
2. Discovery: generated a scoped worklist for `src/Inquiry`, filtered to 65 real source files, and reviewed the high-risk clusters: SQL/parameters, transactions/pipeline, DI, public attributes, converters, and generator touchpoints.
3. Validation: promoted only issues with source/control/sink evidence or clear API-footgun reachability.
4. Attack-path/severity: calibrated against a library threat model. Downstream developers are trusted code authors, but end-user input commonly reaches store parameters, raw SQL strings, collection parameters, and async transaction workflows.

Priority key:

- P1: fix before pre-release public package.
- P2: strongly recommended before release, especially because breaking changes are allowed.
- P3: useful polish, missing feature, or optimization.

## Findings

### P1 - Soft-deleted rows can leak through OR predicates

Status: fixed in `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs` with regression coverage in `tests/Inquiry.Generators.Tests/SoftDeleteGeneratorTests.cs`.

Core trigger: `[InquiryWhere(..., Or = true)]` allows flat OR composition in `src/Inquiry/Stores/InquiryWhereAttribute.cs`, while `[InquirySelectAllByPredicate]` promises the soft-delete filter is AND-composed by default in `src/Inquiry/Stores/InquirySelectAllByPredicateAttribute.cs`.

Original generator evidence: `SqlBuilder.BuildSelectByPredicateSql` appended `AppendWhere(RenderPredicates(...), context.SoftDeleteActivePredicate)` in `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`. `AppendWhere` emitted `whereClause + " AND " + extraPredicate`; `RenderPredicates` emitted flat `A OR B` with no parentheses.

Impact: SQL like `A OR B AND IsDeleted = 0` returns deleted rows that match `A`. That violates the core soft-delete invariant and can expose logically deleted data unless the caller explicitly requested `IncludeDeleted = true`.

Proposed fix: parenthesize composed predicate bodies before adding global filters, at least when any OR is present. Add generator tests for `OR + soft delete`, and check other global filters that reuse `AppendWhere`.

### P1 - Ambient store calls can escape a closed transaction

Status: fixed in `src/Inquiry/DefaultInquiry.cs` and `src/Inquiry/Transactions/InquiryTransaction.cs` with regression coverage in `tests/Inquiry.Sqlite.Tests/AmbientTransactionIntegrationTests.cs`.

Original evidence: `DefaultInquiry.ActivePipeline` fell back to `_defaultPipeline` when the ambient slot existed but `Pipeline` was null (`src/Inquiry/DefaultInquiry.cs`). Root transaction close set `slot.Pipeline = null`. The docs explicitly described straggler async work after close falling through to the default pipeline.

Impact: generated stores resolved from DI can be called by async work that started inside a transaction but resumes after commit/rollback/dispose. Those calls silently execute outside the transaction. That is a dangerous integrity footgun for audit writes, authorization-coupled writes, outbox patterns, and tests that assume "inside this async transaction scope" is atomic.

Proposed fix: replace null-as-default with an explicit closed/pending sentinel in the captured slot. New work after the using block should still be able to use the default pipeline, but async descendants that captured the transaction slot should fail fast after close. A more ergonomic alternative is a transaction-scope API, e.g. `ExecuteInTransactionAsync(Func<IInquiryTransaction, Task>)`, that owns the lifetime and prevents post-close ambient reuse.

### P1 - Root commit/rollback are not serialized with in-flight transaction operations

Status: fixed in `src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs` and `src/Inquiry/Transactions/InquiryTransaction.cs` with regression coverage in `tests/Inquiry.Sqlite.Tests/TransactionStateMachineTests.cs`.

Original evidence: transacted queries, executes, and savepoint operations used `EnterInFlight()` in `src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs`, but root `InquiryTransaction.CommitAsync` and `RollbackAsync` called the provider transaction directly in `src/Inquiry/Transactions/InquiryTransaction.cs`.

Impact: one task can hold a streaming reader while another commits or rolls back the same transaction. That bypasses the guard that exists specifically because `DbConnection` is not thread-safe, causing provider-specific corruption, confusing exceptions, or partially observed transaction behavior.

Proposed fix: share the in-flight guard with the root transaction handle, or route commit/rollback through guarded pipeline methods. Add tests for commit/rollback while a transaction-scoped `QueryAsync` enumerator is active.

### P2 - Batch mutations bypass optimistic concurrency

Status: fixed with generator diagnostic `INQ022` in `src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs` and validation in `src/Inquiry.Generators.Shared/StoreProcessor.cs`. Attribute docs now direct token entities to single-row update/delete APIs.

Evidence: `InquiryConcurrencyTokenAttribute` promises generated UPDATE/DELETE token checks. `InquiryUpdateAllAttribute` documents that batch update does not perform optimistic-concurrency checks, and `InquiryDeleteAllAttribute` exposes key-only batch delete. Supporting generator evidence: batch update builds a key-only `WHERE` in `StoreProcessor.BuildUpdateAllRowTemplate`, and `StoreOperationEmitter.SelectUpdateSetColumns` excludes concurrency tokens from the update.

Impact: any concurrency-token entity with `[InquiryUpdateAll]` or `[InquiryDeleteAll]` can overwrite or delete stale rows without matching or advancing the token. It is documented, but it is still a sharp integrity break in an ORM that otherwise advertises optimistic concurrency.

Proposed fix: before release, either reject batch mutations on concurrency-token entities with a diagnostic, or add concurrency-aware batch APIs that bind original tokens and honor `ThrowOnConcurrencyConflict`.

### P2 - Raw SQL overloads make injection-prone usage easy

Status: partially addressed with `InquirySql.Sql(FormattableString)` in `src/Inquiry/InquirySql.cs`. This gives users an explicit safe factory for ad-hoc SQL while preserving existing raw string overloads. A future breaking API pass should still consider renaming/removing raw string overloads, because C# overload resolution chooses `string` over `FormattableString` when both same-name overloads exist.

Evidence: `IInquiry` exposes raw `string commandText` query/execute overloads; `DefaultInquiry` wraps those strings into `InquiryCommand`; `InquiryRequestPipeline.InitializeCommandSync` assigns `DbCommand.CommandText` directly. Parameter values are safely bound through `DbParameter`, but command text is entirely caller-controlled.

Impact: this is not a generated-SQL injection bug, but consuming apps can easily interpolate end-user input into raw SQL. A micro-ORM should keep raw SQL available, but the safer path should be the most ergonomic path.

Proposed fix: keep `InquirySql.Sql(...)` as the safe interpolation path for now. In a later breaking API pass, consider renaming/removing raw string overloads or adding an analyzer warning for interpolated or non-constant strings passed to raw overloads. Do not add same-name `FormattableString` overloads while raw `string` overloads remain, because C# overload resolution still selects `string` for interpolated string expressions.

### P2 - Unbounded IN and batch parameter expansion can DoS callers or hit provider caps

Status: fixed with `InquiryOptions.MaxParametersPerCommand` and generated/runtime checks in `src/Inquiry/Parameters/InquiryInExpansion.cs` and `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`.

Evidence: `InquiryInExpansion.Expand` builds a new SQL string and one `DbParameter` per collection element without a cap. Batch delete/update docs already tell users to stay under provider parameter limits, but the runtime/generator does not enforce those limits.

Impact: if a user-controlled list reaches `Compare.In`, `[InquiryDeleteAll]`, or a large batch operation, Inquiry can allocate very large command text and parameter sets before the provider fails. This is a local resource-exhaustion and reliability issue, especially in request handlers.

Proposed fix: add provider-aware maximum parameter/IN-list limits through `IInquiryConnectionFactory` or `InquiryOptions`, with conservative defaults. Fail early with a clear exception and document chunking helpers for large lists.

### P2 - AddInquiry scans every loaded assembly and instantiates registrations reflectively

Status: fixed in `src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs` and `src/Inquiry.Generators.Shared/RegistrationEmitter.cs`. `AddInquiry()` now registers only core runtime services; the generator emits `AddInquiryGeneratedStores()` for explicit store/materializer registration, and the explicit assembly overload scans only caller-supplied assemblies.

Evidence: `InquiryServiceCollectionExtensions.AddGeneratedServices` loops through `AppDomain.CurrentDomain.GetAssemblies()`, calls `GetTypes()`, and instantiates every concrete `IInquiryServiceRegistration` with `Activator.CreateInstance(..., nonPublic: true)`.

Impact: this is convenient, but broad. It can register unexpected generated stores from any already-loaded assembly, invokes arbitrary registration constructors at startup, costs reflection over the whole AppDomain, and still misses referenced assemblies that are not loaded yet. Tests already added explicit assembly overloads because of the missed-assembly behavior.

Implemented fix: registration is explicit by default. The generator emits `AddInquiryGeneratedStores()`, `AddInquiry()` registers only core runtime services, and the remaining assembly overload is caller-directed rather than AppDomain-wide.

### P2 - Repeated AddInquiry calls can silently ignore later safety options

Status: fixed in `src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs`; repeated calls now compose configuration delegates into the registered `InquiryOptions` instance.

Evidence: `AddInquiryCore` creates an `InquiryOptions`, invokes the configure delegate, then registers it with `TryAddSingleton`. If one module calls `AddInquiry()` first and a later module calls `AddInquiry(o => o.ThrowOnConcurrencyConflict = true)`, the later configured instance is ignored.

Impact: safety settings such as `ThrowOnConcurrencyConflict` and `PrepareStatements` can appear configured while the service provider still uses defaults.

Proposed fix: make configured repeat calls fail fast, replace/update the existing descriptor deliberately, or move to `IOptions<InquiryOptions>` so standard options composition rules apply.

### P3 - Root transaction close methods can be invoked again after close

Evidence: `InquiryTransaction.CommitAsync` and `RollbackAsync` check only `_disposed`; they do not check `_closed`, even though `Close()` records the first terminal transition.

Impact: double commit, rollback-after-commit, or commit-after-rollback calls through to the underlying `DbTransaction` again, leaving behavior provider-specific.

Proposed fix: check `_closed` in commit/rollback and either throw `ObjectDisposedException` consistently or make the operations explicitly idempotent.

### P3 - Streaming interceptor failures are inconsistent

Evidence: streaming `QueryAsync` invokes initialized/executing interceptors before entering the inner execute/read/materialize failure blocks. Buffered paths wrap the full body and report failures to `CommandFailedAsync`.

Impact: an interceptor exception during streaming setup is invisible to failure interceptors, while the same interceptor failure in buffered execution is reported. That makes diagnostics and tracing inconsistent.

Proposed fix: define interceptor failure semantics and make streaming and buffered paths match. If interceptor failures should not call failure interceptors, make both paths skip them; otherwise wrap setup consistently.

### P3 - Retry backoff is unbounded and trusts jitter

Evidence: `RetryingConnectionOpener.NextDelay` multiplies base delay by exponential factor and `1.0 + _jitter()`, then casts to ticks.

Impact: very large `maxAttempts` or `baseDelay`, or a custom jitter delegate returning an invalid value, can overflow, throw, or sleep much longer than intended.

Proposed fix: validate jitter output, cap max delay, and clamp or checked-convert ticks. Expose a max-delay option on provider retry options.

### P3 - Schema-qualified foreign keys cannot be expressed

Evidence: `InquiryTableAttribute` has `Schema`, but `InquiryForeignKeyAttribute` carries only `ReferencedTable` and `ReferencedColumn`.

Impact: generated DDL cannot correctly express cross-schema or non-default-schema foreign keys.

Proposed fix: add `ReferencedSchema`, or better, a type-based foreign key overload that can derive table, schema, and key column from the referenced entity type.

### P3 - Dialect marker fails late for invalid names

Evidence: `InquiryDialectAttribute` stores `Name` without null/whitespace validation, and its docs say unknown values leave partial store methods without implementations.

Impact: typos or casing mistakes produce confusing generator/build failures.

Proposed fix: add generator diagnostics for null, empty, and unknown dialect names. Also consider public constants such as `InquiryDialects.Sqlite` so attribute usage is less stringly typed.

## Missing features and API improvements

1. Add a typed transaction API. The ambient model is elegant, but users need a safer option for critical workflows: `ExecuteInTransactionAsync`, store methods that accept `IInquiryTransaction`, or generated transaction-bound store wrappers.
2. Support multiple named database contexts. The single global `IInquiryConnectionFactory` intentionally blocks multiple providers in one service provider, but many apps need read/write split, tenant databases, or two stores against different engines. A pre-release breaking change could introduce `IInquiry<TContext>` and provider registrations keyed by context.
3. Continue the safe SQL interpolation API pass. `InquirySql.Sql(FormattableString)` now provides a safe path; a later breaking change can make the raw SQL surface more explicit and analyzer-backed.
4. Expand stored procedure support. Docs already list missing OUT/INOUT parameters, scalar returns, multiple result sets, table-valued parameters, and Oracle ref cursors.
5. Make JSON conversion configurable and AOT-friendly. `InquiryJsonConverter<T>` uses default `JsonSerializer` options and no context. Add attribute or options support for a `JsonSerializerOptions`/`JsonTypeInfo` provider.
6. Make batch APIs first-class around limits and concurrency. Provide chunking helpers, provider caps, and concurrency-aware batch shapes instead of asking callers to remember all limits.
7. Improve multi-assembly generated-registration ergonomics. `AddInquiryGeneratedStores()` is explicit for one generated assembly, and `AddInquiry(params Assembly[])` remains as a fallback; multi-assembly apps may still want a more discoverable generated name or host-level helper.

## Optimizations

1. Cache ad-hoc parameter object accessors. `InquiryParameterReader` reflects public properties and invokes getters on every ad-hoc call. Cache readable property metadata or compiled accessors per parameter type.
2. Consider bounded list pre-sizing where counts are cheaply known. Buffered query paths currently allocate `new List<T>()`; generated methods with known page size could allocate capacity in keyset/offset cases.

## Coverage notes

No direct parameter-value SQL injection was found in the binder path: `InquiryParameterBinder` creates provider parameters and assigns values through `DbParameter.Value`.

No network, file-system, process execution, deserialization-of-arbitrary-type, SSRF, auth, or path traversal product surfaces exist in the core runtime package.

This document started as a review-only artifact. The P1 items and several P2 items are now implemented with targeted generator/runtime tests; remaining proposed fixes should still be implemented with focused coverage before release.
