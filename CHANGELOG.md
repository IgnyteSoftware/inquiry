# Changelog

All notable changes to Inquiry are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- `[InquiryInsert]` and `[InquiryUpdate]` now infer batch operations from entity collection parameters. The separate `[InquiryInsertAll]` and `[InquiryUpdateAll]` attributes were removed.

## [1.0.0-preview.10] - 2026-09-04

### Added

- Compile-time query composition now supports nested `AND`/`OR` groups, negation, optional predicates, reusable specifications, filtered aggregates, predicate paging, and SQL `SET` expressions.
- Generated store-method XML documentation now includes the provider-specific SQL used by the method. IntelliSense labels operations that use multiple commands and retains XML comments from the defining partial declaration.

### Changed

- Mutation methods now compose `[InquiryUpdate]` or `[InquiryDelete]` with `[InquiryWhere]`. Partial updates infer their target columns from leading parameter names, while table-wide deletion requires `[InquiryDeleteAll]`.

## [1.0.0-preview.9] - 2026-08-29

### Added

- The new `Ignyte.Inquiry.Aspire` package registers SQL Server, PostgreSQL, MySQL, MariaDB, SQLite, or Oracle from an Aspire connection-resource name. Each registration uses the provider's shared data source or connection factory and adds Inquiry telemetry and health checks.
- Provider dependency-injection extensions now accept externally owned shared data sources for SQL Server, PostgreSQL, MySQL, MariaDB, SQLite, and Oracle.

### Fixed

- Generated typed data-reader access now honors the `DbDataReader` null contract and rejects null accessor entries.

### Documentation

- [Aspire integration](docs/site/articles/features/aspire.md) covers package setup, connection-resource registration, defaults, and existing data-source registration.

## [1.0.0-preview.8] - 2026-08-17

### Added

- Native bulk insert is now transactional, configurable, observable, and allocation-efficient: it enlists in the ambient Inquiry transaction on SQL Server and PostgreSQL; on MySQL/MariaDB, whose security-isolated `AllowLoadLocalInfile` bulk connection cannot be shared, a bulk insert inside a transaction throws before any rows are written. Per-call `InquiryBulkInsertOptions` cover timeout, batch size, table lock, progress notification, and connection behavior; options a provider cannot honor throw before writing, and the SQLite/Oracle batch-SQL fallback rejects non-null options. Telemetry adds connection-open and copy-duration histograms, per-phase span events, and an enlisted/dedicated connection-mode tag; the generator emits typed per-column accessors so the PostgreSQL binary COPY path and the bulk row reader avoid per-cell boxing.

### Changed

- Package dependency floors raised: `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite`, and `System.Configuration.ConfigurationManager` to 10.0.11, `MySqlConnector` to 2.6.2, `Oracle.ManagedDataAccess.Core` to 23.26.300, `SQLitePCLRaw.lib.e_sqlite3` to 3.53.3, and `Respawn` (Testing package) to 7.0.0.

## [1.0.0-preview.7] - 2026-08-03

### Fixed

- Provider packages' symbol packages (`.snupkg`) failed nuget.org validation (first seen on `1.0.0-preview.6`): analyzer PDBs rode in the snupkg at `lib/net8.0/` with no matching `lib/` DLL. Analyzer assemblies (`Inquiry.*.Analyzer`, `Inquiry.Generators.Shared`) now embed their debug info and SourceLink (`DebugType=embedded`), the snupkg carries only the runtime `lib/` PDBs, and the release verifier checks the embedded PDB identity and SourceLink instead of the loose-PDB layout.

## [1.0.0-preview.6] - 2026-08-03

First changelog-tracked preview. Earlier previews (`1.0.0-preview.3` through `.5`) predate this
changelog and the immutable-tag ruleset; their artifacts remain on nuget.org.

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
- Runtime-parameterized global filters via `[InquiryGlobalFilter(ContextKey = "…")]`, bound from an ambient `InquiryFilterContext.BeginScope(…)` instead of a constant column. Reads without an ambient scope throw `InquiryFilterValueMissingException`; misconfiguration is a build error (INQ093). Key-based writes do not compose the filter by default; set-based predicate writes do.
- PostgreSQL row-level-security session helpers: `SetLocalAsync` on `IInquiryTransaction` sets a transaction-scoped custom GUC (via parameterized `set_config(…, true)`) that an RLS policy reads with `current_setting`. Transaction-only by design — applied outside a transaction the setting simply does not survive to the next statement (`SET LOCAL` warns and no-ops; `set_config(…, true)` expires with the implicit statement transaction), so policies read an unset parameter and queries silently return zero rows. The setting is discarded at commit or rollback, so no `RESET`/`DISCARD` is needed.
- Opt-in write-side enforcement via `[InquiryGlobalFilter(EnforceOnWrites = true)]`: the filter's predicate is AND-composed onto key-based update, delete, hard delete, restore, batch delete, and hard predicate delete, so a write aimed at a row the filter hides affects zero rows. Inserts stay unfiltered and the column is never auto-stamped; upsert on such an entity is a build error (INQ095) because its insert branch cannot be filtered. Rows-affected 0 now conflates not-found, stale concurrency token, and filtered-out. Stores that do not opt in generate byte-identical SQL.
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
- Roslyn analyzer diagnostics (INQ001–INQ095) for compile-time validation.

### Changed

- The repository moved from a personal account to the [IgnyteSoftware](https://github.com/IgnyteSoftware) organization. Issue, pull-request, and documentation URLs now live under `github.com/IgnyteSoftware/inquiry`; the packages themselves are unaffected.

### Documentation

- [Column encryption](docs/site/articles/features/column-encryption.md) — application-side encryption as a value converter (encrypt in `ToProvider`, decrypt in `FromProvider`), with a worked `AesGcm` example, the loss of filtering and indexing on the ciphertext, and the SQL Server Always Encrypted / PostgreSQL pgcrypto alternatives. No new API; the existing converter seam carries it.
- [GraphQL DataLoader recipe](docs/site/articles/features/graphql-dataloader.md) — batching Hot Chocolate resolver fan-out onto a single `Compare.In` predicate method.
- [Migrations recipe](docs/site/articles/features/migrations.md) — using `InquiryGeneratedSchema.Ddl` as the baseline script for DbUp or FluentMigrator, plus `ProviderArtifactsDdl` and `ProviderArtifactsValidationSql` for deploying and checking the SQL Server TVP types.

[Unreleased]: https://github.com/IgnyteSoftware/inquiry/compare/v1.0.0-preview.10...HEAD
[1.0.0-preview.10]: https://github.com/IgnyteSoftware/inquiry/compare/v1.0.0-preview.9...v1.0.0-preview.10
[1.0.0-preview.9]: https://github.com/IgnyteSoftware/inquiry/compare/v1.0.0-preview.8...v1.0.0-preview.9
[1.0.0-preview.8]: https://github.com/IgnyteSoftware/inquiry/compare/v1.0.0-preview.7...v1.0.0-preview.8
[1.0.0-preview.7]: https://github.com/IgnyteSoftware/inquiry/compare/v1.0.0-preview.6...v1.0.0-preview.7
[1.0.0-preview.6]: https://github.com/IgnyteSoftware/inquiry/releases/tag/v1.0.0-preview.6
