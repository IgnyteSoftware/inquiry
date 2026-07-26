# Architecture investigation tickets

Status: future investigation backlog. These are not committed 1.0 roadmap items and should not displace current P0/P1 correctness, provider, packaging, and validation gates.

Source review: 2026-07-13 architectural review of `InquiryStore<T>` vs `IInquiryStore<T>` and Inquiry's fit for Modular Monolith / Vertical Slice Architecture applications.

## Ticket 1: Additive `IInquiryStore<TEntity>` marker/interface compatibility

Problem:
`InquiryStore<TEntity>` is both the generator marker and the base class that provides the protected `IInquiry` facade. Some application architectures may want stores to inherit an application base class, but replacing `InquiryStore<TEntity>` outright would be source-breaking and would remove the protected runtime seam.

Investigation:
- Should `InquiryStore<TEntity>` implement a new marker `IInquiryStore<TEntity>`?
- Should the generator discover stores declared as `IInquiryStore<TEntity>` without deriving from `InquiryStore<TEntity>`?
- If interface-only stores are supported, should the generator emit a private `IInquiry` field, a constructor, and generated method bodies that use that field?
- What diagnostics are needed for ambiguous entity types, missing constructors, custom methods that reference `Inquiry`, or classes that implement multiple `IInquiryStore<T>` variants?

Evidence:
- `src/Inquiry/Stores/InquiryStore.cs` owns the protected `Inquiry` property and constructor injection.
- `src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs` currently uses a syntactic base-list predicate for `InquiryStore`.
- `src/Inquiry.Generators.Shared/Infrastructure/GeneratorHelpers.cs` semantically walks base types to recover `TEntity`.
- `src/Inquiry.Generators.Shared/StoreProcessor.cs` emits the store constructor and calls `base(inquiry)`.

Likely outcome:
Keep `InquiryStore<TEntity>` as the primary contract. Consider `IInquiryStore<TEntity>` only as an additive marker and possible future opt-in path for interface-only stores.

## Ticket 2: Module-aware generated store registration

Problem:
`AddInquiryGeneratedStores()` is assembly-level and convenient, but modular monoliths often want explicit module registration methods that compose cleanly in host startup and make ownership obvious.

Investigation:
- Should the generator emit an optional module-named extension, such as `AddSalesInquiryStores()`, based on assembly name or an assembly attribute?
- Should registration expose metadata listing registered stores, entities, and schema manifest hash per module?
- How should multi-assembly applications compose generated registrations without reflection or AOT risk?
- Should docs recommend one `Add{Module}Infrastructure()` wrapper per module today?

Evidence:
- `src/Inquiry.Generators.Shared/RegistrationEmitter.cs` emits one `AddInquiryGeneratedStores()` extension and direct scoped store/materializer registrations.
- `src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs` keeps `AddInquiry()` core-only and marks assembly scanning as trimming-unsafe.
- `tests/Inquiry.Tests/AddInquiryAssemblyOverloadTests.cs` pins explicit assembly scanning as fallback only.

Likely outcome:
Add docs first. Later consider an assembly attribute to customize generated registration extension names and expose module metadata.

## Ticket 3: Named databases/providers for module-owned persistence

Problem:
Inquiry intentionally binds one `IInquiryConnectionFactory` per service collection. That prevents accidental mixed-provider misuse, but blocks a clean single-container modular monolith where different modules own different databases or provider configurations.

Investigation:
- Design keyed/named `IInquiry` registrations, e.g. `AddInquiryDatabase("orders", ...)`.
- Decide how generated stores choose their database: assembly attribute, store attribute, module registration wrapper, or keyed DI at registration time.
- Define transaction semantics for same-name stores, different-name stores, and cross-database operations.
- Preserve the existing single-provider default as the safe path.

Evidence:
- `src/Inquiry/DependencyInjection/InquiryProviderRegistration.cs` throws if a second connection factory is registered.
- `docs/site/develop/roadmap.md` already tracks named databases/providers as future P2 work.
- `docs/site/articles/features/transactions.md` explicitly scopes Inquiry transactions to one ADO.NET connection/transaction and excludes cross-database DTC.

Likely outcome:
Post-1.0 feature. It is central for mature modular-monolith support, but should not be rushed into the current single-provider runtime.

## Ticket 4: Module schema manifests and drift validation

Problem:
The schema manifest work creates a machine-readable expected schema per generated assembly. Modular applications need this elevated into a module contract: each module owns a schema manifest, deployment validation, and drift policy.

Investigation:
- Define how tooling discovers all module manifests without loading application code.
- Compare each module manifest independently against live database schemas.
- Decide how cross-module foreign keys are represented, allowed, or warned.
- Add docs for CI drift checks in modular monoliths.

Evidence:
- `src/Inquiry.Generators.Shared/SchemaEmitter.cs` now builds `SchemaManifestJson`, a SHA-256, and assembly metadata chunks.
- `src/Inquiry.Generators.Shared/SchemaManifestWriter.cs` defines deterministic manifest transport.
- `docs/site/articles/features/schema-ddl.md` documents manifest metadata and says live/offline validation belongs to the validation workstream.

Likely outcome:
Fold into the live/offline validation tooling work. Avoid a separate migration engine; keep Inquiry focused on expected-schema contracts.

## Ticket 5: Runtime-parameterized tenant filters and write-side enforcement

Problem:
Current global filters are compile-time static boolean predicates. They are useful for active/published gates, but real multi-tenancy often needs tenant id parameters from ambient context and write-side protection for by-key mutations.

Investigation:
- Add runtime-parameterized filters, such as `[InquiryTenantFilter(nameof(TenantId))]`, bound from an ambient tenant provider.
- Decide if filters are named and selectively suppressible.
- Define write-side enforcement for key-based update/delete/restore and returning mutations.
- Explore PostgreSQL row-level-security helpers that set tenant context per transaction/connection.

Evidence:
- `docs/site/articles/features/global-filters.md` documents current static read-side behavior and warns that by-key writes are not filtered.
- `docs/site/develop/roadmap.md` already notes parameterized and named filters plus PostgreSQL RLS helpers as a future gap.

Likely outcome:
High-value product maturity work for SaaS and modular applications. Treat write-side tenant enforcement as a safety feature, not just query convenience.

## Ticket 6: Module-aware observability and interceptors

Problem:
Telemetry currently records database system, operation, optional SQL text, affected rows, and errors. Modular systems need module/store/method labels, and interceptors may need to target only one module or database.

Investigation:
- Add generated command metadata for module, store, method, entity, and operation.
- Surface metadata through `InquiryCommandContext` without adding hot-path allocations when no interceptors are registered.
- Allow registering interceptors globally, per named database, or per module.
- Decide how sqlcommenter and OpenTelemetry tags should expose module data.

Evidence:
- `src/Inquiry/Diagnostics/InquiryTelemetryInterceptor.cs` currently derives operation from SQL text and db system from command type.
- `src/Inquiry/Interceptors/IInquiryCommandInterceptor.cs` is the central interception seam.
- `docs/site/articles/features/observability.md` and `docs/site/articles/features/interceptors.md` document the current opt-in behavior.

Likely outcome:
Pair this with named providers/module registration. It is an adoption multiplier for production modular systems.

## Ticket 7: Cross-module coupling analyzer

Problem:
Modular monolith and vertical slice applications rely on boundaries that are easy to erode accidentally: stores can reference another module's entities, schemas, generated interfaces, or tables without an explicit architectural decision.

Investigation:
- Add optional analyzer diagnostics for cross-module entity/store references.
- Define module identity: assembly, namespace prefix, custom attribute, or generated registration group.
- Allow explicit exceptions for shared-kernel modules and deliberate integration boundaries.
- Detect schema ownership smells, such as two modules mapping the same physical table with `GenerateDdl=true`.

Evidence:
- `docs/site/articles/architecture.md` documents one dialect per assembly and generated DDL per assembly.
- `src/Inquiry.Generators.Shared/SchemaEmitter.cs` already detects duplicate physical schema mappings for DDL purposes.
- Modular review found no current first-class boundary analyzer beyond compile-time mapping diagnostics.

Likely outcome:
Start as opt-in analyzer lints. Keep the default permissive so small applications are not burdened.

## Ticket 8: Vertical slice transaction and testing recipes

Problem:
The runtime transaction model is already strong, but the docs do not yet frame it for vertical slices: command handlers, pipeline behaviors, slice-level unit of work, savepoints, outbox writes, and test isolation.

Investigation:
- Add recipes for handler-level `ExecuteInTransactionAsync`.
- Document how generated stores resolved before a transaction join through ambient routing.
- Show savepoint usage for nested slice operations.
- Add testing guidance for transaction-per-test sandbox and module fixtures.
- Clarify how outbox libraries should use `tx.Connection` and `tx.Transaction`.

Evidence:
- `src/Inquiry/DefaultInquiry.cs` uses an `AsyncLocal` ambient transaction slot.
- `src/Inquiry/Transactions/IInquiryTransaction.cs` exposes borrowed connection/transaction interop.
- `docs/site/articles/features/transactions.md` already documents ambient store routing, savepoints, and outbox interop.
- `docs/site/articles/features/testing.md` lists a future Ecto-style transaction sandbox.

Likely outcome:
Docs first, then testing package support. This is low risk and helps users succeed with the architecture Inquiry already has.

## Ticket 9: Competitor-positioning review for modular applications

Problem:
Inquiry's modular story should be differentiated deliberately rather than accidentally copying EF Core, Dapper, or Drizzle patterns.

Investigation:
- Compare EF Core per-`DbContext` modules, Dapper repository conventions, Drizzle schema modules, Prisma schema/client generation, Marten/Wolverine sessions, and sqlx/sqlc validation.
- Identify where Inquiry's compile-time SQL, schema manifest, AOT-safe generated registration, and generated store interfaces are uniquely strong.
- Decide which competitor features should remain out of scope.
- Produce positioning docs and product acceptance criteria for modular-monolith support.

Evidence:
- `docs/site/develop/roadmap.md` already compares several competitor patterns for validation, diagnostics, read replicas, filters, scaffolding, and cloud modes.
- `docs/site/develop/design-notes.md` defines Inquiry as compile-time SQL, not a stateful ORM, runtime LINQ provider, or migration engine.

Likely outcome:
Use this as a strategy/design brief before implementing named providers or module analyzers.
