# Adding a New Database Provider (Foundation / F8)

A provider is two projects mirroring the existing three (SQLite / SQL Server / PostgreSQL):
`Inquiry.<Dialect>` (runtime: connection factory + `AddInquiry<Dialect>` DI extension + `[assembly: InquiryDialect("<Dialect>")]`) and `Inquiry.<Dialect>.Analyzer` (the `[Generator]` + `<Dialect>SqlBuilder`).

Use SQLite as the copy template (smallest surface). Beyond the new project files, a provider must touch
this **fixed set of shared append-points**. They are append-only by design, so two providers added in
parallel worktrees (e.g. MySQL + Oracle) only conflict if they edit the *same* line — keep additions at
the end of each list.

## Checklist

1. **`Directory.Packages.props`** — add a `<PackageVersion Include="…" />` for the ADO.NET provider
   (e.g. `MySqlConnector`, `Oracle.ManagedDataAccess.Core`). Append near the related entries.
2. **`Inquiry.slnx`** — register the new runtime, analyzer, and test projects.
3. **`samples/Inquiry.Northwind/NorthwindSchema.cs`** — add a `<Dialect>Ddl` field with the 13-table
   schema in that dialect (model on the PostgreSql block). Append at the end.
4. **`tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`** — register the new generator in the
   `generators[]` array and add the runtime assembly to `GetReferences()`. **These two spots are the
   most likely cross-provider merge collision** — coordinate or land providers back-to-back.
5. **`tests/Inquiry.<Dialect>.Tests`** — new opt-in integration project: a `<Dialect>FactAttribute`
   gated on an `INQUIRY_<DIALECT>_CONNECTION_STRING` env var + a `<Dialect>TestHarness`, cloning the
   PostgreSql test project. Must **skip** (not fail) when the env var is unset so the default build
   stays green without a live server.

## SqlBuilder obligations

The new `<Dialect>SqlBuilder : SqlBuilder` must implement every member. Per the F3 convention, feature
workstreams add new capabilities as `virtual`-with-base-default where the SQL is dialect-uniform, so a
new provider inherits those for free and only overrides what genuinely differs (identifier quoting,
upsert syntax, pagination clause, parameter prefix, etc.). If a workstream adds an `abstract` member,
every provider — including any in-flight new one — must implement it before the build is green; prefer
landing such workstreams before, or coordinating with, an in-flight provider.

## No shared runtime/abstraction change

A well-behaved provider needs **zero** edits to `Inquiry.Generators.Shared` core or the runtime
pipeline — it fits entirely inside the existing `SqlBuilder` contract + `IInquiryConnectionFactory`.
(Exceptions are documented per-workstream, e.g. Oracle's `:`-parameter / `BindByName` needs the
command-init hook that ships with W4.)
