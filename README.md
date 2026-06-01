# Inquiry

Inquiry is an experimental .NET 6+ source-generated micro-ORM. You write attributed entity classes and `partial` store classes with `partial` method declarations; a Roslyn source generator emits the matching partial with method bodies, materializers, and dependency-injection wiring. Every SQL string is built at compile time by a provider-specific `SqlBuilder` and baked into the generated source as `const string` fields, so each database can be tuned independently and the runtime carries no SQL.

> **New here?** Start with **[docs/STATUS.md](docs/STATUS.md)** — current state, development process, and what's left to do. This README is the architecture deep-dive.

## Repository Layout

| Project | Purpose |
| --- | --- |
| `src/Inquiry` | Public runtime: `IInquiry` facade, request pipeline, attributes, command/parameter types, transactions, and DI extension `AddInquiry()`. Ships no SQL — every statement is built at compile time. |
| `src/Inquiry.Generators.Shared` | Roslyn incremental source-generator framework. Discovers entities and stores; emits materializers, generated stores, a DI registration class, and `InquiryGeneratedSchema.Ddl`. Owns the per-dialect `SqlBuilder` hierarchy that produces the baked SQL. Bundled privately into each provider analyzer. |
| `src/Inquiry.{Sqlite,SqlServer,PostgreSql,MySql,Oracle}.Analyzer` | Per-dialect Roslyn analyzer assemblies — each is a `[Generator]` that bundles `Inquiry.Generators.Shared` and emits only when its dialect matches the resolved `[InquiryDialect]`. |
| `src/Inquiry.Sqlite` | SQLite provider: `SqliteInquiryConnectionFactory`, `AddInquirySqlite(...)`, and `[assembly: InquiryDialect("Sqlite")]`. |
| `src/Inquiry.SqlServer` | SQL Server provider: equivalent factory, DI extension, and dialect marker. |
| `src/Inquiry.PostgreSql` | PostgreSQL provider: equivalent factory, DI extension, and dialect marker. |
| `src/Inquiry.MySql` | MySQL / MariaDB provider: equivalent factory, DI extension, and dialect marker. |
| `src/Inquiry.Oracle` | Oracle provider: equivalent factory, DI extension, and dialect marker. |
| `tests/Inquiry.Tests` | Core runtime tests (pipeline, parameter binding, transactions). |
| `tests/Inquiry.Generators.Tests` | Source-generator tests + per-dialect SQL assertions. |
| `tests/Inquiry.IntegrationTesting` | Shared test-support library: the canonical expected-Northwind schema contract, the schema-fidelity comparator, and the `ISchemaIntrospector` abstraction. |
| `tests/Inquiry.Sqlite.Tests` | End-to-end integration + schema-fidelity tests against in-memory SQLite (no Docker). |
| `tests/Inquiry.{SqlServer,PostgreSql,MySql,Oracle}.Tests` | End-to-end Northwind + generated-DDL + fidelity tests. Each compiles Northwind under its own dialect and runs against a real engine via Testcontainers; skips gracefully when Docker is absent. |
| `samples/Inquiry.Northwind` | Shared classic-Northwind entities, stores, and per-provider DDL (`SqliteDdl`, `SqlServerDdl`, `PostgreSqlDdl`, `MySqlDdl`, `OracleDdl`) consumed by every sample and integration-test project. |
| `samples/Inquiry.Sample` | Runnable ASP.NET Core sample exercising CRUD, upsert, transactions, and eager loading on SQLite. |

## Authoring an Entity and Store

```csharp
using Inquiry;
using Inquiry.Entities;
using Inquiry.Stores;

[InquiryTable("TOrganization")]
public sealed class Organization
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn]
    public bool IsActive { get; set; } = true;
}

public partial class OrganizationStore : InquiryStore<Organization>
{
    [InquirySelectAll]
    public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct = default);

    [InquirySelectAllByField("IsActive")]
    public partial IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken ct = default);

    [InquiryInsert]   public partial Task<int>  InsertAsync(Organization o, CancellationToken ct = default);
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<Organization?> InsertReturningAsync(Organization o, CancellationToken ct = default);

    [InquiryUpdate]   public partial Task<bool> UpdateAsync(Organization o, CancellationToken ct = default);
    [InquiryUpdate(ReturnEntity = true)]
    public partial Task<Organization?> UpdateReturningAsync(Organization o, CancellationToken ct = default);

    [InquiryUpsert]   public partial Task<int>  UpsertAsync(Organization o, CancellationToken ct = default);
    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<Organization?> UpsertReturningAsync(Organization o, CancellationToken ct = default);

    [InquiryDeleteOneByKey] public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken ct = default);
}
```

Register Inquiry with a provider and resolve the store:

```csharp
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;

services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

// later
var orgs = sp.GetRequiredService<OrganizationStore>();
await foreach (var o in orgs.SelectAllAsync()) { /* ... */ }
```

## Supported Store Attributes

All store attributes live in `Inquiry.Stores`. The method must be a `partial` declaration, the enclosing class must be `partial`, and the last parameter must be `CancellationToken`. The generator emits the constructor and the method bodies into a second partial of the same class — no derived class, no user-written constructor.

| Attribute | Required signature | Maps to |
| --- | --- | --- |
| `[InquirySelectAll]` | `IAsyncEnumerable<TEntity>` | `BuildSelectAllSql` |
| `[InquirySelectAllEager]` | `IAsyncEnumerable<TEntity>` | `BuildSelectAllSql` on parent + `BuildSelectAllSql` on each child relation, grouped in-memory by FK |
| `[InquirySelectOneByKey]` | `Task<TEntity?>(TKey key, …)` | `BuildSelectByKeySql` |
| `[InquirySelectOneByKeyEager]` | `Task<TEntity?>(TKey key, …)` | `BuildSelectByKeySql` on parent + `BuildSelectByFieldSql` on each child relation |
| `[InquirySelectAllByField("FieldName")]` | `IAsyncEnumerable<TEntity>(TField value, …)` | `BuildSelectByFieldSql` |
| `[InquiryInsert]` | `Task<int>(TEntity e, …)` | `BuildInsertSql` |
| `[InquiryInsert(ReturnEntity = true)]` | `Task<TEntity?>(TEntity e, …)` | `BuildInsertReturningSql` |
| `[InquiryUpdate]` | `Task<bool>(TEntity e, …)` | `BuildUpdateSql` |
| `[InquiryUpdate(ReturnEntity = true)]` | `Task<TEntity?>(TEntity e, …)` | `BuildUpdateReturningSql` |
| `[InquiryUpsert]` | `Task<int>(TEntity e, …)` | `BuildUpsertSql` |
| `[InquiryUpsert(ReturnEntity = true)]` | `Task<TEntity?>(TEntity e, …)` | `BuildUpsertReturningSql` |
| `[InquiryDeleteOneByKey]` | `Task<bool>(TKey key, …)` | `BuildDeleteByKeySql` |
| `[InquiryStoredProcedure("ProcName")]` | `IAsyncEnumerable<T>` / `Task<T?>` / `Task<int>` | Raw `InquiryCommand` with `CommandType.StoredProcedure` |

Entity-mapping attributes live in `Inquiry.Entities`: `[InquiryTable]`, `[InquiryColumn]`, `[InquiryKey]`, `[InquiryForeignKey]`, `[InquiryRelation]`.

> Beyond this core CRUD surface, Inquiry also supports richer WHERE predicates, ORDER BY + offset/keyset pagination, batch & bulk operations, projections + aggregations, optimistic concurrency, soft deletes, full-text search, JSON/array/value-converter columns, and `CREATE TABLE` schema-DDL generation. Each has a self-contained spec under [`docs/plans/`](docs/plans); see [`docs/STATUS.md`](docs/STATUS.md) for the full feature inventory.

---

## Flow 1: Source Generation (Compile Time)

The generator runs once per provider analyzer assembly loaded by Roslyn — each is a concrete `[Generator]` subclass of [`InquiryGeneratorBase`](src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs) (e.g. [`InquirySqliteGenerator`](src/Inquiry.Sqlite.Analyzer/InquirySqliteGenerator.cs)). At compile time the base walks the user's compilation, builds models, decides whether this provider's dialect matches the resolved `[InquiryDialect]`, and (if so) emits three kinds of `.g.cs` files.

### Step 1 — Discover candidate classes

[`src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs`](src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs)

Hooks `SyntaxProvider` to pull every `ClassDeclarationSyntax` that has either an attribute list or a base type. Cheap predicate keeps the incremental pipeline fast.

### Step 2 — Discover entities

[`src/Inquiry.Generators.Shared/EntityProcessor.cs`](src/Inquiry.Generators.Shared/EntityProcessor.cs)

For each candidate, reads `[InquiryTable]`, then scans the type for `[InquiryColumn]`, `[InquiryKey]`, `[InquiryForeignKey]`, and `[InquiryRelation]`. Populates [`EntityModel`](src/Inquiry.Generators.Shared/Models/EntityModel.cs) (table name, schema, columns, key, relations). Reports diagnostics for missing keys, multiple keys, or invalid types.

For every discovered entity, immediately emits `<Entity>InquiryEntityMaterializer.g.cs` — a sealed class implementing [`IInquiryEntityMaterializer<TEntity>`](src/Inquiry/Materialization/IInquiryEntityMaterializer.cs) that reads ordinals once and projects a `DbDataReader` row into a `TEntity`.

### Step 3 — Discover store methods

[`src/Inquiry.Generators.Shared/StoreProcessor.cs`](src/Inquiry.Generators.Shared/StoreProcessor.cs) drives store discovery; [`StoreOperationEmitter.cs`](src/Inquiry.Generators.Shared/StoreOperationEmitter.cs) is the per-method dispatcher.

For each `partial` method declaration on each `partial class : InquiryStore<TEntity>`:

1. `StoreOperationEmitter.GetOperation` identifies the Inquiry attribute and returns a [`StoreOperation`](src/Inquiry.Generators.Shared/Models/StoreOperation.cs) enum value.
2. `StoreOperationEmitter.Validate` checks the return type, parameter count, parameter types, and (for `[InquirySelectAllByField]`) confirms the named field exists in the entity. Reports diagnostics on mismatch.
3. A [`StoreMethodModel`](src/Inquiry.Generators.Shared/Models/StoreMethodModel.cs) is collected.

### Step 4 — Emit the concrete store

[`StoreProcessor.Emit`](src/Inquiry.Generators.Shared/StoreProcessor.cs) writes `<Store>.InquiryStore.g.cs`. Each generated store has the same shape:

```csharp
// Second partial of the user's class — generated alongside the user-authored one.
partial class OrganizationStore
{
    // One const string per statement the store actually needs. SQL is built at compile time
    // by the dialect-matched SqlBuilder and baked into the source.
    private const string _sqlSelectAll = "SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\"";
    private const string _sqlSelectByKey = "SELECT \"Key\", \"Name\", \"IsActive\" FROM \"TOrganization\" WHERE \"Key\" = @Key";
    private const string _sqlInsert = "INSERT INTO \"TOrganization\" (\"Key\", \"Name\", \"IsActive\") VALUES (@Key, @Name, @IsActive)";
    private const string _sqlUpdate = "UPDATE \"TOrganization\" SET \"Name\" = @Name, \"IsActive\" = @IsActive WHERE \"Key\" = @Key";
    private const string _sqlDeleteByKey = "DELETE FROM \"TOrganization\" WHERE \"Key\" = @Key";

    public OrganizationStore(IInquiry inquiry) : base(inquiry) { }

    public partial IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken ct)
        => Inquiry.QueryAsync<Organization, OrganizationInquiryEntityStructMaterializer>(
            new InquiryCommand(_sqlSelectAll), default, ct);

    public partial async Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct)
        => await Inquiry.QuerySingleOrDefaultAsync<Organization, OrganizationInquiryEntityStructMaterializer>(
            new InquiryCommand(_sqlSelectByKey, new[] { new InquiryParameter("@Key", key) }),
            default, ct).ConfigureAwait(false);

    // ... one partial implementation per attributed method
}
```

Key points:

- **Every SQL string is built at compile time by the generator-side `SqlBuilder`.** The runtime ships zero SQL — no dialect type, no per-call build, no constructor work. Each statement is emitted as a `private const string`.
- **Only the SQL the store actually uses is emitted.** A store that only declares `[InquiryInsert]` gets exactly one const.
- **Eager-load attributes** emit additional `_sql_<Relation>` / `_sql_<Relation>_All` const fields, each built via the same builder calls for the child entity.
- **Stored procedures** bypass the builder entirely; the generator emits a raw `InquiryCommand` with the procedure name and `CommandType.StoredProcedure`.

### Step 5 — Emit the DI registration

[`src/Inquiry.Generators.Shared/RegistrationEmitter.cs`](src/Inquiry.Generators.Shared/RegistrationEmitter.cs)

Writes `InquiryGeneratedServiceRegistration.g.cs`: a sealed `Inquiry.Generated.InquiryGeneratedServiceRegistration : IInquiryServiceRegistration` whose `AddServices(IServiceCollection)` calls `TryAddSingleton` for every generated materializer and `TryAddScoped` for every store the generator emitted into.

At application startup [`InquiryServiceCollectionExtensions.AddInquiry()`](src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs) loops over all loaded assemblies, reflects out every concrete `IInquiryServiceRegistration`, and invokes `AddServices`. That is how generator output crosses the assembly boundary into your DI container without any manual wiring.

### Generator output summary

For a project with N entities and M attributed stores, the generator produces:

- `N` files named `<Entity>.InquiryEntity.g.cs` (materializers)
- `M` files named `<Store>.InquiryStore.g.cs` (concrete stores)
- `1` file named `InquiryGeneratedServiceRegistration.g.cs` (DI hookup)

---

## Flow 2: SQL Building (Compile Time)

All SQL is produced at compile time by an internal `SqlBuilder` hierarchy in `Inquiry.Generators`. The Inquiry runtime ships **zero SQL** — no abstract dialect, no per-call build, no statement cache. Each generated store carries the SQL strings it needs as `private const string` fields.

### The shape

```csharp
// src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs
public abstract class SqlBuilder
{
    public abstract string DialectName { get; }
    public abstract string QuoteIdentifier(string identifier);
    public virtual  string ParameterName(string logical);     // default: "@" + logical
    public          string QuoteTable(string? schema, string table);

    public abstract string BuildSelectAllSql       (SqlBuildContext ctx);
    public abstract string BuildSelectByKeySql     (SqlBuildContext ctx);
    public abstract string BuildSelectByFieldSql   (SqlBuildContext ctx, IReadOnlyList<IColumn> filterColumns);
    public abstract string BuildInsertSql          (SqlBuildContext ctx);
    public abstract string BuildInsertReturningSql (SqlBuildContext ctx);
    public abstract string BuildUpdateSql          (SqlBuildContext ctx);
    public abstract string BuildUpdateReturningSql (SqlBuildContext ctx);
    public abstract string BuildDeleteByKeySql     (SqlBuildContext ctx);
    public abstract string BuildUpsertSql          (SqlBuildContext ctx);
    public abstract string BuildUpsertReturningSql (SqlBuildContext ctx);
}
```

`StoreProcessor` constructs a `SqlBuildContext` once per (entity, builder) pair and feeds it to whichever `Build…Sql` methods the store actually needs. The context precomputes the SQL fragments those methods consume:

- `Table` — quoted `[schema].[table]`
- `SelectColumns` — comma-joined quoted column list
- `InsertColumns`, `InsertParameters` — insertable columns + matching `@name` parameters
- `SetClauses` — `col = @col, ...` for non-key non-generated columns
- `QuotedKeyColumns`, `KeyParameters`, `KeyWhereClause`
- The raw `IColumn` lists, so builders can introspect (e.g., to emit `OUTPUT INSERTED.*` or `RETURNING`).

### Builder implementations

| File | Identifier quoting | Upsert strategy |
| --- | --- | --- |
| [`src/Inquiry.Sqlite.Analyzer/SqliteSqlBuilder.cs`](src/Inquiry.Sqlite.Analyzer/SqliteSqlBuilder.cs) | `"name"` (double quotes, doubled to escape) | `INSERT ... ON CONFLICT DO UPDATE` |
| [`src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs`](src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs) | `[name]` (brackets, `]` doubled to escape) | `MERGE INTO ... WHEN MATCHED / WHEN NOT MATCHED` |
| [`src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs`](src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs) | `"name"` (double quotes) | `INSERT … ON CONFLICT (...) DO UPDATE` |
| [`src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs`](src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs) | `` `name` `` (backticks, doubled to escape) | `INSERT … ON DUPLICATE KEY UPDATE` |
| [`src/Inquiry.Oracle.Analyzer/OracleSqlBuilder.cs`](src/Inquiry.Oracle.Analyzer/OracleSqlBuilder.cs) | `"name"` (double quotes) | `MERGE INTO … WHEN MATCHED / WHEN NOT MATCHED` |

Each `Inquiry.<Provider>.Analyzer` assembly is a self-contained Roslyn source generator. It bundles a private copy of the shared framework ([`src/Inquiry.Generators.Shared`](src/Inquiry.Generators.Shared)) — the framework cannot be a separate analyzer because Roslyn loads each provider's analyzer into its own `AssemblyLoadContext`, so static state and type identity do not cross provider boundaries. The provider analyzer DLL ships alongside the matching runtime DLL inside the provider's NuGet (`analyzers/dotnet/cs/`), and is wired into project-reference dev builds by [`Directory.Build.targets`](Directory.Build.targets).

To change how a CRUD statement is emitted for one database without affecting the others, override the matching `Build…Sql` method in that provider's builder.

### Dialect selection

Each provider's analyzer ([`InquiryGeneratorBase`](src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs)) hardcodes its own dialect name. When Roslyn loads the analyzer (because the consumer referenced the matching provider package), it inspects the compilation for `[assembly: InquiryDialect("...")]` — first on the consuming assembly (explicit override), then on referenced assemblies (provider runtime DLLs ship this attribute pre-applied). If the resolved name matches the generator's own dialect, it emits; otherwise it stays silent so a coexisting provider can claim the build. If no dialect attribute is found at all, the loaded generator treats that as implicit opt-in to its own dialect. Multiple matching dialects surface as `INQ014`, emitted once by the alphabetically-first provider in the conflict.

### Provider DI registration

Each provider package ships its own service-collection extension that registers only the connection factory:

```csharp
// src/Inquiry.Sqlite/DependencyInjection/SqliteInquiryServiceCollectionExtensions.cs
services.AddSingleton<IInquiryConnectionFactory>(_ => new SqliteInquiryConnectionFactory(connectionString));
```

The generated store ctor takes only `IInquiry` — no dialect dependency to inject because every SQL string was baked in at compile time.

---

## Flow 3: Query Execution (Runtime)

Calling a generated store method drives this path:

```
GeneratedFooStore.SelectByKeyAsync(key)
        │
        ▼
IInquiry.QuerySingleOrDefaultAsync<T>(_sqlSelectByKey, new { key })   ← string was built at ctor time
        │  ─ DefaultInquiry resolves an IInquiryEntityMaterializer<T> from DI
        ▼
IInquiryRequestPipeline.QuerySingleOrDefaultAsync(command, materialize)
        │  ─ opens connection via IInquiryConnectionFactory
        │  ─ creates DbCommand, binds parameters (InquiryParameterBinder)
        │  ─ notifies IInquiryCommandInterceptor's BeforeExecuteAsync
        │  ─ ExecuteReaderAsync, project rows via materializer
        │  ─ notifies AfterExecuteAsync / OnFailureAsync
        ▼
TEntity (or IAsyncEnumerable<TEntity>) returned to caller
```

The relevant files:

| File | Role |
| --- | --- |
| [`src/Inquiry/IInquiry.cs`](src/Inquiry/IInquiry.cs) | Public facade: `QueryAsync`, `QuerySingleOrDefaultAsync`, `ExecuteAsync`, `BeginTransactionAsync`. |
| [`src/Inquiry/DefaultInquiry.cs`](src/Inquiry/DefaultInquiry.cs) | Resolves a materializer from `IServiceProvider` and delegates to the pipeline. |
| [`src/Inquiry/Pipeline/IInquiryRequestPipeline.cs`](src/Inquiry/Pipeline/IInquiryRequestPipeline.cs) | Low-level contract: takes an `InquiryCommand` + `Func<DbDataReader, T>` materializer. |
| [`src/Inquiry/Pipeline/InquiryRequestPipeline.cs`](src/Inquiry/Pipeline/InquiryRequestPipeline.cs) | Owns ADO.NET: connection open, command setup, reader loop, interceptor notifications, dispose. |
| [`src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs`](src/Inquiry/Pipeline/TransactedInquiryRequestPipeline.cs) | Variant that reuses a single connection + `DbTransaction` for the lifetime of a transaction. |
| [`src/Inquiry/Connections/IInquiryConnectionFactory.cs`](src/Inquiry/Connections/IInquiryConnectionFactory.cs) | Returns an opened `DbConnection`. Provider packages implement this with their concrete `DbConnection`. |
| [`src/Inquiry/Commands/InquiryCommand.cs`](src/Inquiry/Commands/InquiryCommand.cs) | Immutable carrier of command text, parameters, and `CommandType`. |
| [`src/Inquiry/Parameters/InquiryParameterReader.cs`](src/Inquiry/Parameters/InquiryParameterReader.cs) | Turns anonymous objects / dictionaries / `InquiryParameter[]` into a uniform `InquiryParameter[]`. |
| [`src/Inquiry/Parameters/InquiryParameterBinder.cs`](src/Inquiry/Parameters/InquiryParameterBinder.cs) | Attaches `InquiryParameter[]` to a `DbCommand`, normalizing prefixes (`@`, `:`, `$`). |
| [`src/Inquiry/Materialization/IInquiryEntityMaterializer.cs`](src/Inquiry/Materialization/IInquiryEntityMaterializer.cs) | Per-entity row projector. The generator emits one per `[InquiryTable]` entity. |
| [`src/Inquiry/Interceptors/IInquiryCommandInterceptor.cs`](src/Inquiry/Interceptors/IInquiryCommandInterceptor.cs) | Optional hook for logging, tracing, or mutating command settings before execution. |

### Eager loading

`[InquirySelectOneByKeyEager]` and `[InquirySelectAllEager]` issue separate queries per relation and stitch the results together in-memory. The generator emits one extra `await foreach` per relation, plus a dictionary-grouping step in the all-eager case. SQL for the child queries is built via the same `BuildSelectByFieldSql` / `BuildSelectAllSql` dialect calls — there is no special "eager" SQL path.

### Transactions

[`IInquiry.BeginTransactionAsync`](src/Inquiry/IInquiry.cs) opens a connection, begins a `DbTransaction`, and returns an [`IInquiryTransaction`](src/Inquiry/Transactions/IInquiryTransaction.cs) whose `Inquiry` property is a `TransactedInquiry`. Generated store methods invoked through that `tx.Inquiry` use a `TransactedInquiryRequestPipeline` that reuses the open connection + transaction for every command until `CommitAsync` / `RollbackAsync` (or `DisposeAsync`).

---

## Running the sample

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The sample seeds an in-process SQLite database, exposes a small HTML dashboard at `/`, and a handful of JSON endpoints under `/api/...` that exercise CRUD, upsert, eager loading, and a transactional insert.

## Running the tests

```powershell
dotnet test
```

Tests cover: parameter binding, the request pipeline, transactions, generator emission, per-dialect SQL strings, end-to-end CRUD/eager-loading against in-memory SQLite, and — for every provider — live CRUD, schema-fidelity, and generated-DDL verification against the real engine.

The SQL Server, PostgreSQL, MySQL, and Oracle integration suites provision their engine with **[Testcontainers](https://dotnet.testcontainers.org/)** — the only host dependency is **Docker**. Each suite starts one container per test assembly, compiles Northwind under its own dialect, creates a throwaway database/schema per test, runs the matching `NorthwindSchema.*Ddl`, and tears it down so parallel tests cannot collide. When Docker is unavailable every live fact **skips** (via `Xunit.SkippableFact`) rather than failing, so `dotnet test` stays green on a machine without Docker.

CI runs PostgreSQL / MySQL / SQL Server on every PR and Oracle nightly. See [`docs/STATUS.md`](docs/STATUS.md) for the current state, development process, and remaining work.
