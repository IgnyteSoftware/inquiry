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
    public partial Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken ct = default);
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

## What's next

Planned additions on the [roadmap](../../develop/roadmap.md): an Ecto-style transaction sandbox
(each test inside a rolled-back transaction, enabling parallel database tests) and declarative
test-data factories.
