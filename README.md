# Inquiry

Inquiry is an experimental .NET 6+ source-generated micro-ORM. You write attributed entity classes and `partial` store classes with `partial` method declarations; a Roslyn source generator emits the matching partial with method bodies, materializers, and dependency-injection wiring. Every SQL string is built at compile time by a provider-specific `SqlBuilder` and baked into the generated source as `const string` fields, so each database can be tuned independently and the runtime carries no SQL.

## Repository Layout

| Project | Purpose |
| --- | --- |
| `src/Inquiry` | Public runtime: `IInquiry` facade, request pipeline, attributes, command/parameter types, transactions, and DI extension `AddInquiry()`. Ships no SQL — every statement is built at compile time. |
| `src/Inquiry.Generators` | Roslyn incremental source generator. Discovers entities and stores, emits materializers, generated stores, and a DI registration class. Also owns the per-dialect `SqlBuilder` hierarchy that produces the SQL baked into generated stores. |
| `src/Inquiry.Sqlite` | SQLite provider: `SqliteInquiryConnectionFactory`, `AddInquirySqlite(...)`, and `[assembly: InquiryDialect("Sqlite")]`. |
| `src/Inquiry.SqlServer` | SQL Server provider: equivalent factory, DI extension, and dialect marker. |
| `src/Inquiry.PostgreSql` | PostgreSQL provider: equivalent factory, DI extension, and dialect marker. |
| `tests/Inquiry.Tests` | Core runtime tests (pipeline, parameter binding, transactions). |
| `tests/Inquiry.Generators.Tests` | Source-generator tests + per-dialect SQL assertions. |
| `tests/Inquiry.Sqlite.Tests` | End-to-end integration tests against in-memory SQLite. |
| `tests/Inquiry.SqlServer.Tests` | End-to-end Northwind integration tests against a real SQL Server (opt-in via `INQUIRY_SQLSERVER_CONNECTION_STRING`). |
| `tests/Inquiry.PostgreSql.Tests` | End-to-end Northwind integration tests against a real PostgreSQL (opt-in via `INQUIRY_POSTGRESQL_CONNECTION_STRING`). |
| `samples/Inquiry.Northwind` | Shared classic-Northwind entities, stores, and per-provider DDL (`SqliteDdl`, `SqlServerDdl`, `PostgreSqlDdl`) consumed by every sample and integration-test project. |
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

---

## Flow 1: Source Generation (Compile Time)

The generator is `InquiryGenerator : IIncrementalGenerator`. At compile time it walks the user's compilation, builds models, and emits three kinds of `.g.cs` files.

### Step 1 — Discover candidate classes

[`src/Inquiry.Generators/InquiryGenerator.cs`](src/Inquiry.Generators/InquiryGenerator.cs)

Hooks `SyntaxProvider` to pull every `ClassDeclarationSyntax` that has either an attribute list or a base type. Cheap predicate keeps the incremental pipeline fast.

### Step 2 — Discover entities

[`src/Inquiry.Generators/EntityProcessor.cs`](src/Inquiry.Generators/EntityProcessor.cs)

For each candidate, reads `[InquiryTable]`, then scans the type for `[InquiryColumn]`, `[InquiryKey]`, `[InquiryForeignKey]`, and `[InquiryRelation]`. Populates [`EntityModel`](src/Inquiry.Generators/Models/EntityModel.cs) (table name, schema, columns, key, relations). Reports diagnostics for missing keys, multiple keys, or invalid types.

For every discovered entity, immediately emits `<Entity>InquiryEntityMaterializer.g.cs` — a sealed class implementing [`IInquiryEntityMaterializer<TEntity>`](src/Inquiry/Materialization/IInquiryEntityMaterializer.cs) that reads ordinals once and projects a `DbDataReader` row into a `TEntity`.

### Step 3 — Discover store methods

[`src/Inquiry.Generators/StoreProcessor.cs`](src/Inquiry.Generators/StoreProcessor.cs) drives store discovery; [`StoreOperationEmitter.cs`](src/Inquiry.Generators/StoreOperationEmitter.cs) is the per-method dispatcher.

For each `partial` method declaration on each `partial class : InquiryStore<TEntity>`:

1. `StoreOperationEmitter.GetOperation` identifies the Inquiry attribute and returns a [`StoreOperation`](src/Inquiry.Generators/Models/StoreOperation.cs) enum value.
2. `StoreOperationEmitter.Validate` checks the return type, parameter count, parameter types, and (for `[InquirySelectAllByField]`) confirms the named field exists in the entity. Reports diagnostics on mismatch.
3. A [`StoreMethodModel`](src/Inquiry.Generators/Models/StoreMethodModel.cs) is collected.

### Step 4 — Emit the concrete store

`StoreProcessor.Emit` writes `<Store>.InquiryStore.g.cs`. Each generated store has the same shape:

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

[`src/Inquiry.Generators/RegistrationEmitter.cs`](src/Inquiry.Generators/RegistrationEmitter.cs)

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
// src/Inquiry.Generators/Sql/SqlBuilder.cs
internal abstract class SqlBuilder
{
    public abstract string DialectName { get; }
    public abstract string QuoteIdentifier(string identifier);
    public virtual  string ParameterName(string logical);     // default: "@" + logical
    public          string QuoteTable(string? schema, string table);

    public abstract string BuildSelectAllSql       (SqlBuildContext ctx);
    public abstract string BuildSelectByKeySql     (SqlBuildContext ctx);
    public abstract string BuildSelectByFieldSql   (SqlBuildContext ctx, IReadOnlyList<ColumnModel> filterColumns);
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
- The raw `ColumnModel` lists, so builders can introspect (e.g., to emit `OUTPUT INSERTED.*` or `RETURNING`).

### Builder implementations

| File | Identifier quoting | Upsert strategy |
| --- | --- | --- |
| [`src/Inquiry.Generators/Sql/SqliteSqlBuilder.cs`](src/Inquiry.Generators/Sql/SqliteSqlBuilder.cs) | `"name"` (double quotes, doubled to escape) | `INSERT ... ON CONFLICT DO UPDATE` |
| [`src/Inquiry.Generators/Sql/SqlServerSqlBuilder.cs`](src/Inquiry.Generators/Sql/SqlServerSqlBuilder.cs) | `[name]` (brackets, `]` doubled to escape) | `MERGE INTO ... WHEN MATCHED / WHEN NOT MATCHED` |
| [`src/Inquiry.Generators/Sql/PostgreSqlSqlBuilder.cs`](src/Inquiry.Generators/Sql/PostgreSqlSqlBuilder.cs) | `"name"` (double quotes) | `INSERT … ON CONFLICT (...) DO UPDATE` |

To change how a CRUD statement is emitted for one database without affecting the others, override the matching `Build…Sql` method in that builder.

### Dialect selection

Each provider runtime assembly declares `[assembly: InquiryDialect("Sqlite")]` (or `"SqlServer"` / `"PostgreSql"`). At generator time, [`DialectResolver`](src/Inquiry.Generators/Sql/DialectResolver.cs) walks the consuming compilation's referenced assemblies, picks the single matching dialect name, and instantiates the corresponding `SqlBuilder`. Ambiguity or missing markers surface as `INQ013` / `INQ014` / `INQ015` diagnostics.

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

Tests cover: parameter binding, the request pipeline, transactions, generator emission, per-dialect SQL strings, and end-to-end CRUD/eager-loading against in-memory SQLite.

The SQL Server and PostgreSQL integration suites build with the rest of the solution but skip every fact unless their provider's connection string is exported:

```powershell
$env:INQUIRY_SQLSERVER_CONNECTION_STRING   = "Server=.;Database=master;Integrated Security=true;TrustServerCertificate=true"
$env:INQUIRY_POSTGRESQL_CONNECTION_STRING  = "Host=localhost;Database=postgres;Username=postgres;Password=postgres"
```

The harnesses point at the named admin database (`master` / `postgres`), create a throwaway database per test, run `NorthwindSchema.SqlServerDdl` / `NorthwindSchema.PostgreSqlDdl`, and drop the database on teardown — parallel tests cannot collide.
