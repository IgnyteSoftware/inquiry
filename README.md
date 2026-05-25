# Inquiry

Inquiry is an experimental .NET 6+ source-generated micro-ORM. You write attributed entity classes and abstract store classes; a Roslyn source generator emits the concrete store implementations, materializers, and dependency-injection wiring. Every SQL string is produced by a provider-specific dialect, so each database can be tuned independently.

## Repository Layout

| Project | Purpose |
| --- | --- |
| `src/Inquiry` | Public runtime: `IInquiry` facade, request pipeline, attributes, command/parameter types, transactions, the `InquirySqlDialect` abstract base, and DI extension `AddInquiry()`. |
| `src/Inquiry.Generators` | Roslyn incremental source generator. Discovers entities and stores, emits materializers, generated stores, and a DI registration class. |
| `src/Inquiry.Sqlite` | SQLite provider: `SqliteInquiryConnectionFactory`, `SqliteInquirySqlDialect`, `AddInquirySqlite(...)`. |
| `src/Inquiry.SqlServer` | SQL Server provider: equivalent factory, dialect, and `AddInquirySqlServer(...)`. |
| `src/Inquiry.PostgreSql` | PostgreSQL provider: equivalent factory, dialect, and `AddInquiryPostgreSql(...)`. |
| `tests/Inquiry.Tests` | Core runtime tests (pipeline, parameter binding, transactions). |
| `tests/Inquiry.Generators.Tests` | Source-generator tests + per-dialect SQL assertions. |
| `tests/Inquiry.Sqlite.Tests` | End-to-end integration tests against in-memory SQLite. |
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

public abstract partial class OrganizationStore : InquiryStore<Organization>
{
    protected OrganizationStore(IInquiry inquiry) : base(inquiry) { }

    [InquirySelectAll]
    public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken ct = default);

    [InquirySelectOneByKey]
    public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct = default);

    [InquirySelectAllByField("IsActive")]
    public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken ct = default);

    [InquiryInsert]   public abstract Task<int>  InsertAsync(Organization o, CancellationToken ct = default);
    [InquiryUpdate]   public abstract Task<bool> UpdateAsync(Organization o, CancellationToken ct = default);
    [InquiryUpsert]   public abstract Task<int>  UpsertAsync(Organization o, CancellationToken ct = default);
    [InquiryDeleteOneByKey] public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken ct = default);
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

All store attributes live in `Inquiry.Stores`. The method must be `abstract`, the enclosing class must be `partial`, and the last parameter must be `CancellationToken`.

| Attribute | Required signature | Maps to |
| --- | --- | --- |
| `[InquirySelectAll]` | `IAsyncEnumerable<TEntity>` | `BuildSelectAllSql` |
| `[InquirySelectAllEager]` | `IAsyncEnumerable<TEntity>` | `BuildSelectAllSql` on parent + `BuildSelectAllSql` on each child relation, grouped in-memory by FK |
| `[InquirySelectOneByKey]` | `Task<TEntity?>(TKey key, …)` | `BuildSelectByKeySql` |
| `[InquirySelectOneByKeyEager]` | `Task<TEntity?>(TKey key, …)` | `BuildSelectByKeySql` on parent + `BuildSelectByFieldSql` on each child relation |
| `[InquirySelectAllByField("FieldName")]` | `IAsyncEnumerable<TEntity>(TField value, …)` | `BuildSelectByFieldSql` |
| `[InquiryInsert]` | `Task<int>(TEntity e, …)` | `BuildInsertSql` |
| `[InquiryUpdate]` | `Task<bool>(TEntity e, …)` | `BuildUpdateSql` |
| `[InquiryUpsert]` | `Task<int>(TEntity e, …)` | `BuildUpsertSql` |
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

For each abstract method on each `partial class : InquiryStore<TEntity>`:

1. `StoreOperationEmitter.GetOperation` identifies the Inquiry attribute and returns a [`StoreOperation`](src/Inquiry.Generators/Models/StoreOperation.cs) enum value.
2. `StoreOperationEmitter.Validate` checks the return type, parameter count, parameter types, and (for `[InquirySelectAllByField]`) confirms the named field exists in the entity. Reports diagnostics on mismatch.
3. A [`StoreMethodModel`](src/Inquiry.Generators/Models/StoreMethodModel.cs) is collected.

### Step 4 — Emit the concrete store

`StoreProcessor.Emit` writes `<Store>.InquiryStore.g.cs`. Each generated store has the same shape:

```csharp
public sealed class GeneratedOrganizationStore : OrganizationStore
{
    // Column metadata — one static array per entity (and per related child entity).
    private static readonly InquirySqlColumn[] _columns = { /* one entry per [InquiryColumn] / [InquiryKey] */ };

    // One readonly string field per statement the store actually needs.
    private readonly string _sqlSelectAll;
    private readonly string _sqlSelectByKey;
    private readonly string _sqlInsert;
    private readonly string _sqlUpdate;
    private readonly string _sqlUpsert;
    private readonly string _sqlDeleteByKey;
    private readonly string _sqlSelectBy_IsActive;     // one per [InquirySelectAllByField]

    public GeneratedOrganizationStore(IInquiry inquiry, InquirySqlDialect sqlDialect) : base(inquiry)
    {
        if (sqlDialect is null) throw new ArgumentNullException(nameof(sqlDialect));
        var _ctx = sqlDialect.CreateContext(/*schema*/ null, "TOrganization", _columns);
        _sqlSelectAll         = sqlDialect.BuildSelectAllSql(_ctx);
        _sqlSelectByKey       = sqlDialect.BuildSelectByKeySql(_ctx);
        _sqlInsert            = sqlDialect.BuildInsertSql(_ctx);
        _sqlUpdate            = sqlDialect.BuildUpdateSql(_ctx);
        _sqlUpsert            = sqlDialect.BuildUpsertSql(_ctx);
        _sqlDeleteByKey       = sqlDialect.BuildDeleteByKeySql(_ctx);
        _sqlSelectBy_IsActive = sqlDialect.BuildSelectByFieldSql(_ctx, _columns[/*index*/]);
    }

    public override IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken ct = default)
        => Inquiry.QueryAsync<Organization>(_sqlSelectAll, ct);

    public override async Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct = default)
        => await Inquiry.QuerySingleOrDefaultAsync<Organization>(_sqlSelectByKey, new { key }, ct);

    // ... one override per attributed method
}
```

Key points:

- **Every SQL string is produced by the provider's dialect.** The generator emits a single `CreateContext` call followed by one `Build…Sql(...)` call per statement the store needs. There is no intermediate builder, no statement-set bag, no central CRUD compiler in the core package.
- **Statements are built once, in the constructor, and cached as `readonly string` fields.** No SQL is rebuilt per call.
- **Only the SQL the store actually uses is emitted.** A store that only declares `[InquiryInsert]` gets exactly one field.
- **Eager-load attributes** generate additional `private static readonly InquirySqlColumn[] _columns_<Relation>` arrays and `_sql_<Relation>` / `_sql_<Relation>_All` fields, each built via the same dialect calls for the child entity.
- **Stored procedures** bypass the dialect entirely; the generator emits a raw `InquiryCommand` with the procedure name and `CommandType.StoredProcedure`.

### Step 5 — Emit the DI registration

[`src/Inquiry.Generators/RegistrationEmitter.cs`](src/Inquiry.Generators/RegistrationEmitter.cs)

Writes `InquiryGeneratedServiceRegistration.g.cs`: a sealed `Inquiry.Generated.InquiryGeneratedServiceRegistration : IInquiryServiceRegistration` whose `AddServices(IServiceCollection)` calls `TryAddSingleton` for every generated materializer and `TryAddTransient` for every generated store.

At application startup [`InquiryServiceCollectionExtensions.AddInquiry()`](src/Inquiry/DependencyInjection/InquiryServiceCollectionExtensions.cs) loops over all loaded assemblies, reflects out every concrete `IInquiryServiceRegistration`, and invokes `AddServices`. That is how generator output crosses the assembly boundary into your DI container without any manual wiring.

### Generator output summary

For a project with N entities and M attributed stores, the generator produces:

- `N` files named `<Entity>.InquiryEntity.g.cs` (materializers)
- `M` files named `<Store>.InquiryStore.g.cs` (concrete stores)
- `1` file named `InquiryGeneratedServiceRegistration.g.cs` (DI hookup)

---

## Flow 2: SQL Building (Provider Packages)

All SQL is owned by a single abstract class — [`InquirySqlDialect`](src/Inquiry/Sql/InquirySqlDialect.cs) in `Inquiry.Sql`. The core package declares the shape; each provider package supplies the bodies.

### The shape

```csharp
public abstract class InquirySqlDialect
{
    public abstract string Name { get; }
    public abstract string QuoteIdentifier(string identifier);
    public virtual  string ParameterName(string logical);           // default: "@" + logical
    public          string QuoteTable(string? schema, string table);

    public InquirySqlBuildContext CreateContext(
        string? schema,
        string tableName,
        IReadOnlyList<InquirySqlColumn> columns);                   // validates + precomputes

    public abstract string BuildSelectAllSql       (InquirySqlBuildContext ctx);
    public abstract string BuildSelectByKeySql     (InquirySqlBuildContext ctx);
    public abstract string BuildSelectByFieldSql   (InquirySqlBuildContext ctx, InquirySqlColumn column);
    public abstract string BuildInsertSql          (InquirySqlBuildContext ctx);
    public abstract string BuildUpdateSql          (InquirySqlBuildContext ctx);
    public abstract string BuildDeleteByKeySql     (InquirySqlBuildContext ctx);
    public abstract string BuildUpsertSql          (InquirySqlBuildContext ctx);
}
```

`CreateContext` validates that exactly one column is marked `[InquiryKey]` and that at least one column is not database-generated, then builds an [`InquirySqlBuildContext`](src/Inquiry/Sql/InquirySqlBuildContext.cs). That context holds precomputed fragments every `Build…Sql` method needs:

- `Table` — quoted `[schema].[table]`
- `SelectColumns` — comma-joined quoted column list
- `InsertColumns`, `InsertParameters` — insertable columns + matching `@name` parameters
- `SetClauses` — `col = @col, ...` for non-key non-generated columns
- `QuotedKeyColumn`, `KeyParameter`
- The raw [`InquirySqlColumn`](src/Inquiry/Sql/InquirySqlColumn.cs) lists, so dialects can introspect (e.g., to emit `OUTPUT INSERTED.*` or `RETURNING`).

### Provider implementations

| File | Identifier quoting | Upsert strategy |
| --- | --- | --- |
| [`src/Inquiry.Sqlite/SqliteInquirySqlDialect.cs`](src/Inquiry.Sqlite/SqliteInquirySqlDialect.cs) | `"name"` (double quotes, doubled to escape) | `INSERT OR REPLACE` |
| [`src/Inquiry.SqlServer/SqlServerInquirySqlDialect.cs`](src/Inquiry.SqlServer/SqlServerInquirySqlDialect.cs) | `[name]` (brackets, `]` doubled to escape) | `MERGE INTO ... WHEN MATCHED / WHEN NOT MATCHED` |
| [`src/Inquiry.PostgreSql/PostgreSqlInquirySqlDialect.cs`](src/Inquiry.PostgreSql/PostgreSqlInquirySqlDialect.cs) | `"name"` (double quotes) | `INSERT … ON CONFLICT (...) DO UPDATE` |

To add a provider or tune one, edit only its `*InquirySqlDialect.cs` file. To change how a CRUD statement is emitted for one database without affecting the others, override the matching `Build…Sql` method in that provider's dialect.

### Provider DI registration

Each provider package ships its own service-collection extension that registers the connection factory and dialect as singletons. Example:

```csharp
// src/Inquiry.Sqlite/DependencyInjection/SqliteInquiryServiceCollectionExtensions.cs
services.AddSingleton<IInquiryConnectionFactory>(_ => new SqliteInquiryConnectionFactory(connectionString));
services.AddSingleton<InquirySqlDialect, SqliteInquirySqlDialect>();
```

The generated store ctor takes both `IInquiry` and `InquirySqlDialect`, so the DI container picks up the provider's dialect automatically.

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
