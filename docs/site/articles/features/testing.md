# Testing

Inquiry ships two test seams: **generated store interfaces** for unit-testing services without a
database, and the **`Inquiry.Testing`** package for integration-testing against real engines.

## Mocking stores: `[InquiryGenerateInterface]`

Generated store methods are non-virtual (no dispatch overhead on the hot path), so the store class
itself cannot be mocked. Opt in to interface generation instead:

```csharp
[InquiryGenerateInterface]
public partial class OrganizationStore : InquiryStore<Organization>
{
    [InquirySelectOneByKey]
    public partial Task<Organization?> SelectByKeyAsync(string key, CancellationToken ct = default);
}
```

The generator additionally emits a `public partial interface IOrganizationStore` with the same
method signatures (default parameter values preserved), declares the store as implementing it, and
registers the interface in DI as a forward to the **same scoped store instance** — so services can
depend on `IOrganizationStore` and tests can hand them a mock, while production keeps direct,
devirtualized calls. The interface is `partial`, so you can extend it with your own members.

## Integration testing: the `Inquiry.Testing` package

Test-framework-agnostic helpers (no xunit/NUnit dependency):

### `SqliteInquiryFixture`

Spins up a uniquely named shared-cache in-memory SQLite database (held alive by a keeper
connection), wires `AddInquiry()` + `AddInquirySqlite(...)`, and lets you add your generated
registrations:

```csharp
await using var fixture = await SqliteInquiryFixture.CreateAsync(
    services => services.AddInquiryGeneratedStores());
await fixture.ExecuteDdlAsync(InquiryGeneratedSchema.Ddl);

using var scope = fixture.CreateScope();
var store = scope.ServiceProvider.GetRequiredService<OrganizationStore>();
```

Each fixture is an isolated database; use one per test (or per test class) — disposal tears it
down.

### `InquirySandbox`

Runs each callback in a fresh DI scope and transaction, then rolls the transaction back on success,
failure, or cancellation. Resolve generated stores from `context.Services` so they share the same
scope and ambient transaction:

```csharp
var sandbox = new InquirySandbox(applicationServices);

await sandbox.RunAsync(async (context, cancellationToken) =>
{
    var organizations = context.Services.GetRequiredService<OrganizationStore>();
    await organizations.InsertAsync(factory.Build("active"), cancellationToken);

    // Visible to this transaction, but absent after RunAsync returns.
    var inserted = await organizations.SelectByKeyAsync("ORG01", cancellationToken);
    Assert.NotNull(inserted);
});
```

The callback receives `context.Transaction`, the scoped `IInquiry` facade for ad-hoc operations.
The root `IInquiryTransaction` handle is deliberately private, so test code cannot commit the
sandbox. Cleanup uses a non-cancelled token and preserves the callback's exception if provider
cleanup also fails.

Generated transactional store methods participate in the sandbox. Native `[InquiryBulkInsert]`
does not: its dedicated provider connection cannot join the ambient transaction, so Inquiry rejects
it inside a sandbox or any other Inquiry transaction. Use `[InquiryInsertAll]` for rollback-safe
test setup.

Nested `RunAsync` calls in the same async context are rejected. When application code deliberately
needs nesting, `context.Transaction.BeginTransactionAsync()` creates a provider savepoint; committing
that savepoint still cannot commit the sandbox's outer transaction. Parallel sandbox runs are most
useful with server databases that support concurrent transactions (such as PostgreSQL or SQL Server).
SQLite still permits only one writer at a time.

### `EntityFactory<TEntity>`

Constructs entities without knowing about stores or persistence. Each factory has its own one-based,
thread-safe sequence. Named states compose in the order passed to `Build`/`BuildMany`:

```csharp
var factory = new EntityFactory<Organization>(sequence => new Organization
    {
        Id = $"ORG{sequence:00}",
        Name = $"Organization {sequence}"
    })
    .State("active", organization => organization.IsActive = true)
    .State("admin", organization => organization.Role = "Admin");

var admins = factory.BuildMany(3, "active", "admin");
```

Bogus works through its normal delegate, so `Inquiry.Testing` does not force a Bogus dependency or
version on consumers:

```csharp
var factory = new EntityFactory<Organization>(() => bogusOrganization.Generate());
```

### `RecordingCommandInterceptor`

An `IInquiryCommandInterceptor` that snapshots every executed command — SQL text, parameter
names/values, rows affected, failures — with assertion helpers:

```csharp
var recorder = new RecordingCommandInterceptor();
// register it: services.AddSingleton<IInquiryCommandInterceptor>(recorder)

await store.InsertAsync(org);
recorder.AssertExecuted("INSERT INTO \"TOrganization\"");
```

`AssertExecuted`/`AssertNotExecuted` throw with the full recorded SQL list on mismatch, so
failures are self-explanatory.

### `InquiryRespawner`

A thin wrapper over [Respawn](https://github.com/jbogard/Respawn) for resetting database state
between tests against the server engines (SQL Server, PostgreSQL, MySQL, Oracle — Respawn does not
support SQLite; use a fresh fixture there):

```csharp
var respawner = await InquiryRespawner.CreateAsync(connectionFactory,
    new RespawnerOptions { DbAdapter = DbAdapter.Postgres });
// ... after each test:
await respawner.ResetAsync(connectionFactory);
```

Resetting deletes data while keeping schema, which is much faster than recreating containers.
