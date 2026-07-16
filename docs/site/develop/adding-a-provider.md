# Adding a provider

A provider is two projects mirroring the existing ones (SQLite / SQL Server / PostgreSQL / MySQL / MariaDB / Oracle):

- **`Inquiry.<Dialect>`** (runtime) — the connection factory, the `AddInquiry<Dialect>(…)` DI extension, and
  `[assembly: InquiryDialect("<Dialect>")]`.
- **`Inquiry.<Dialect>.Analyzer`** (`[Generator]`) — the `<Dialect>SqlBuilder`.

Use SQLite as the copy template (smallest surface). Beyond the new project files, a provider touches a
**fixed set of shared append-points**. They are append-only by design, so two providers added in parallel
worktrees only conflict if they edit the *same* line — keep additions at the end of each list.

## Checklist

1. **`Directory.Packages.props`** — add a `<PackageVersion Include="…" />` for the ADO.NET provider
   (e.g. `MySqlConnector`, `Oracle.ManagedDataAccess.Core`). Append near the related entries.
2. **`Inquiry.slnx`** — register the new runtime, analyzer, and test projects.
3. **`samples/Inquiry.Northwind/NorthwindSchema.cs`** — add a `<Dialect>Ddl` field with the Northwind
   schema in that dialect (model on the PostgreSql block). Append at the end.
4. **`tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`** — register the new generator in the
   `generators[]` array and add the runtime assembly to `GetReferences()`. **These two spots are the most
   likely cross-provider merge collision** — coordinate or land providers back-to-back.
5. **`tests/Inquiry.<Dialect>.Tests`** — a new integration project that **link-compiles the shared
   Northwind source under the new dialect** so it exercises the engine's real SQL. Clone the PostgreSql
   test project's fixtures:
   - a **`<Dialect>ContainerFixture`** that provisions the engine with **Testcontainers** (one container per
     test assembly), plus a collection fixture and a `<Dialect>TestHarness`;
   - a **`<Dialect>SchemaIntrospector`** implementing `ISchemaIntrospector` (from `Inquiry.IntegrationTesting`)
     so the schema-fidelity guardrail can compare the live catalog against the expected Northwind schema;
   - live tests written with **`SkippableFact`** so they **skip** (not fail) when Docker is unavailable,
     keeping the default build green without a live server.

## SqlBuilder obligations

The new `<Dialect>SqlBuilder : SqlBuilder` must implement every member. By convention, feature workstreams
add new capabilities as `virtual`-with-base-default where the SQL is dialect-uniform, so a new provider
inherits those for free and only overrides what genuinely differs (identifier quoting, upsert syntax,
pagination clause, parameter prefix, batch shape, etc.). If a workstream adds an `abstract` member, every
provider — including any in-flight new one — must implement it before the build is green; prefer landing
such workstreams before, or coordinating with, an in-flight provider.

The generator's diagnostic registry reserves IDs per area; current diagnostics include `INQ040`
(unknown relation foreign key), `INQ041` (composite-key child relation), and `INQ042` (invalid `OrderBy`
direction). `INQ027` (projection-on-soft-delete) has been retired — projections now compose the soft-delete
filter.

## No shared runtime/abstraction change

A well-behaved provider needs **zero** edits to the `Inquiry.Generators.Shared` core or the runtime
pipeline — it fits entirely inside the existing `SqlBuilder` contract + `IInquiryConnectionFactory`.
(Exceptions are documented per-workstream — e.g. Oracle's `:`-parameter / `BindByName` needs the
command-init hook, and Oracle's `RETURNING … INTO` ref-cursor handling lives in its connection factory.)

> The full per-workstream design specs are retained in-repo under
> [`docs/plans/`](https://github.com/JakeOverstreet/inquiry/tree/main/docs/plans) as the archived design
> record. See also [Design notes](design-notes.md).
