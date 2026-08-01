# Changelog

All notable changes to Inquiry are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Compile-time SQL source generator with six database providers (SQLite, SQL Server, PostgreSQL, MySQL, MariaDB, Oracle).
- Full CRUD operations: insert, select, update, delete, and upsert with parameterized queries.
- Transactions via `IInquiryTransaction` with `BeginTransactionAsync` and `ExecuteInTransactionAsync`.
- Eager loading for one-to-many, many-to-one, and many-to-many relations via grid reader.
- Many-to-many eager loading across all three junction shapes: an explicitly-mapped junction, a composite-key related entity, and an auto-managed junction whose table the generator synthesizes into the schema DDL. An auto-managed junction is read-only.
- Stored procedure support with OUTPUT, RETURN, INOUT parameters, multiple result sets, Oracle REF CURSOR, and SQL Server TVP parameters.
- Batch mutations with provider-selected transports (TVP, unnest, multi-row VALUES, individual commands).
- Bulk insert via provider-native copy APIs.
- A uniform cancellation contract across single commands and batch DML: when a cancel races provider completion, both a lying success and a native driver error normalize to `OperationCanceledException` carrying the caller's token, with the driver exception preserved as the inner exception. Work that genuinely completed before the token fired is never re-labelled cancelled, including a durably committed batch.
- Paged select with `InquiryPagedResult<T>` paired SELECT+COUNT return shape.
- WHERE predicates: comparison, IN-collection, LIKE, BETWEEN, IS NULL, full-text search, JSON containment.
- JSON-path querying: an `[InquiryWhere]` criterion with a `JsonPath` compares the dialect's JSON extraction of that path against the bound parameter instead of the whole column.
- ORDER BY with offset/keyset pagination.
- Projections, aggregations, DISTINCT queries, and column-list constants for raw SQL.
- Ad-hoc DTO materialization via `InquiryAdHocAttribute`.
- Database view support via `InquiryViewAttribute`.
- Computed columns via `InquiryComputedExpressionAttribute`.
- Generated store interfaces via `InquiryGenerateInterfaceAttribute`.
- Schema DDL generation and live schema fidelity testing.
- Optimistic concurrency via row-version columns.
- Pessimistic row-level locking with `InquiryLockMode` (update, share, skip-locked, no-wait).
- Soft delete with boolean-flag and timestamp indicators, global filter suppression.
- Named global filters via `[InquiryGlobalFilter(Name = "…")]` with a compile-time per-method bypass via `[InquiryIgnoreFilter("…")]`; an unknown, blank, or duplicated name is a build error (INQ091, INQ092) rather than a silently widened query.
- Runtime-parameterized global filters via `[InquiryGlobalFilter(ContextKey = "…")]`, bound from an ambient `InquiryFilterContext.BeginScope(…)` instead of a constant column. Reads without an ambient scope throw `InquiryFilterValueMissingException`; misconfiguration is a build error (INQ093). Key-based writes do not compose the filter in this release; set-based predicate writes do.
- Audit columns (`InquiryCreatedAt`, `InquiryCreatedBy`, `InquiryModifiedAt`, `InquiryModifiedBy`) with `InquiryAuditContext`.
- JSON and array column mapping with provider-native types.
- Value converters for custom CLR-to-database type mappings.
- Sequential GUID generation (COMB) with correct sort order per provider.
- Parameterized `FormattableString` SQL execution via `InquirySql.Sql()`.
- Prepared statement mode control via `PreparedStatementMode`.
- NativeAOT compatibility across all providers.
- Provider compatibility modes: `AzureSql`, `CockroachDb`, `AuroraPostgreSql`, and cloud-hosted modes for MySQL, MariaDB, and Oracle.
- OpenTelemetry tracing, metrics, and ILogger structured logging via `AddInquiryTelemetry()`.
- Transient-fault retry and per-provider backup-server failover.
- Health checks for all providers.
- Data seeding via `IInquiryDataSeeder` with `AddInquirySeeder<T>()` and `SeedInquiryAsync()`.
- Dependency injection with `AddInquiry()` service collection extensions and provider-specific `AddInquirySqlServer()` / `AddInquiryPostgreSql()` / etc. registration.
- ASP.NET Core audit-context middleware via `UseInquiryAuditContext()` for stamping `CreatedBy`/`ModifiedBy` from the current user identity.
- Interceptors package with slow-query logging, sqlcommenter trace-context tagging, and N+1 query detection.
- Testing package with SQLite fixture, recording interceptor, entity factory, transaction sandbox, and Respawn reset.
- 88 Roslyn analyzer diagnostics (INQ001–INQ093) for compile-time validation.

### Changed

- The repository moved from a personal account to the [IgnyteSoftware](https://github.com/IgnyteSoftware) organization. Issue, pull-request, and documentation URLs now live under `github.com/IgnyteSoftware/inquiry`; the packages themselves are unaffected and unreleased.

### Documentation

- [Column encryption](docs/site/articles/features/column-encryption.md) — application-side encryption as a value converter (encrypt in `ToProvider`, decrypt in `FromProvider`), with a worked `AesGcm` example, the loss of filtering and indexing on the ciphertext, and the SQL Server Always Encrypted / PostgreSQL pgcrypto alternatives. No new API; the existing converter seam carries it.
- [GraphQL DataLoader recipe](docs/site/articles/features/graphql-dataloader.md) — batching Hot Chocolate resolver fan-out onto a single `Compare.In` predicate method.
- [Migrations recipe](docs/site/articles/features/migrations.md) — using `InquiryGeneratedSchema.Ddl` as the baseline script for DbUp or FluentMigrator, plus `ProviderArtifactsDdl` and `ProviderArtifactsValidationSql` for deploying and checking the SQL Server TVP types.

[Unreleased]: https://github.com/IgnyteSoftware/inquiry/commits/main
