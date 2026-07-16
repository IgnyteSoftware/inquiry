# Changelog

All notable changes to Inquiry are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Compile-time SQL source generator with six database providers (SQLite, SQL Server, PostgreSQL, MySQL, MariaDB, Oracle).
- Full CRUD operations: insert, select, update, delete with parameterized queries.
- Eager loading for one-to-many, many-to-one, and many-to-many relations via grid reader.
- Stored procedure support with OUTPUT, RETURN, INOUT parameters, multiple result sets, Oracle REF CURSOR, and SQL Server TVP parameters.
- Batch mutations with provider-selected transports (TVP, unnest, multi-row VALUES, individual commands).
- Bulk insert via provider-native copy APIs.
- Paged select with `InquiryPagedResult<T>` paired SELECT+COUNT return shape.
- WHERE predicates: comparison, IN-collection, LIKE, BETWEEN, IS NULL, full-text search, JSON containment.
- ORDER BY with offset/keyset pagination.
- Projections, aggregations, and column-list constants for raw SQL.
- Schema DDL generation and live schema fidelity testing.
- Optimistic concurrency via row-version columns.
- Soft delete with boolean-flag and timestamp indicators, global filter suppression.
- JSON and array column mapping with provider-native types.
- Value converters for custom CLR-to-database type mappings.
- Sequential GUID generation (COMB) with correct sort order per provider.
- NativeAOT compatibility across all providers.
- OpenTelemetry tracing, metrics, and ILogger structured logging via `AddInquiryTelemetry()`.
- Transient-fault retry and per-provider backup-server failover.
- Health checks for all providers.
- Dependency injection with `AddInquiry()` service collection extensions and provider-specific `AddInquirySqlServer()` / `AddInquiryPostgreSql()` / etc. registration.
- Interceptors package with slow-query logging and sqlcommenter trace tagging.
- Testing package with SQLite fixture, recording interceptor, and Respawn reset.
- 86 Roslyn analyzer diagnostics (INQ001–INQ086) for compile-time validation.

[Unreleased]: https://github.com/JakeOverstreet/inquiry/commits/main
