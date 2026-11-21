# Aspire integration

Each Aspire provider package registers an Inquiry provider from the connection name that Aspire
passes to a project. It also registers Inquiry tracing, metrics, logging, and the `inquiry` readiness
health check.

| Provider | Aspire package | Registration |
|---|---|---|
| SQL Server | `Ignyte.Inquiry.Aspire.SqlServer` | `AddInquirySqlServer` |
| PostgreSQL | `Ignyte.Inquiry.Aspire.PostgreSql` | `AddInquiryPostgreSql` |
| MySQL | `Ignyte.Inquiry.Aspire.MySql` | `AddInquiryMySql` |
| MariaDB | `Ignyte.Inquiry.Aspire.MariaDb` | `AddInquiryMariaDb` |
| Oracle | `Ignyte.Inquiry.Aspire.Oracle` | `AddInquiryOracle` |
| SQLite | `Ignyte.Inquiry.Aspire.Sqlite` | `AddInquirySqlite` |

Each package depends on its provider's runtime package and on `Ignyte.Inquiry.Aspire`, which holds
the provider-neutral telemetry and health-check defaults. An application restores only the driver of
the provider it uses.

Install the Aspire package for your provider, and add a direct reference to the provider package so
the application gets its source generator. The Aspire package excludes the generator from
transitive flow. Do not directly reference multiple provider packages in the same application,
because each contains a dialect-specific source generator.

```powershell
dotnet add package Ignyte.Inquiry.Aspire.PostgreSql --prerelease
dotnet add package Ignyte.Inquiry.PostgreSql --prerelease
```

Define the database and pass its connection to the application in the AppHost:

```csharp
var postgres = builder.AddPostgres("postgres")
    .AddDatabase("orders");

builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(postgres)
    .WaitFor(postgres);
```

Use the same resource name in the application project:

```csharp
using Inquiry.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddInquiry();
builder.Services.AddInquiryGeneratedStores();
builder.AddInquiryPostgreSql("orders");
```

`AddInquiryPostgreSql` reads `ConnectionStrings:orders`, builds and registers one
`NpgsqlDataSource`, and configures Inquiry to open connections from that shared pool. Aspire
supplies the setting for the `WithReference(postgres)` call. The extension also:

- calls `AddInquiryTelemetry()`;
- subscribes OpenTelemetry to the `Inquiry` activity source and meter;
- calls `AddHealthChecks().AddInquiry()`.

The other Aspire provider packages have the same resource-name interface. The methods are in the
`Microsoft.Extensions.Hosting` namespace, the same namespace as `IHostApplicationBuilder`.

Each method reads the connection string when it runs, not when the first connection opens. A missing
or blank `ConnectionStrings:<name>` value throws `InvalidOperationException` from the `AddInquiry*`
call, so a misconfigured application fails at startup.

## Test hosts

Because the connection string is read during registration, a `WebApplicationFactory<TEntryPoint>`
test must supply its value before the application's `Program` runs. Use `UseSetting` in
`ConfigureWebHost`:

```csharp
public sealed class OrdersApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:orders", TestDatabase.ConnectionString);
    }
}
```

The factory passes `UseSetting` values to the application as command-line arguments. The
application must pass its `args` to `WebApplication.CreateBuilder(args)`. If it calls
`CreateBuilder()` without them, the application does not see the test value, content root, or
environment. The `AddInquiry*` call then registers the original connection string, or throws when
no `appsettings.json` is found in the working directory.

The key is `ConnectionStrings:` followed by the resource name passed to `AddInquiry*`. The value
takes precedence over `appsettings.json`, user secrets, and environment variables, so the
application registers its data source against the test database. Every Inquiry store and the
`inquiry` health check then use that database.

A configuration source that `Program` adds after `CreateBuilder` and before `AddInquiry*`, such
as Azure Key Vault or an extra JSON file, overrides the test value. If that source holds the
connection string, skip the source in tests or set the test value in it.

Do not use `ConfigureAppConfiguration` or `ConfigureTestServices` for this setting. With
`WebApplication.CreateBuilder`, the factory applies those callbacks after `Program` has called
`AddInquiry*`. The data source has already been built from the original connection string by then,
and the test still uses the original database. If no value is configured at all, the `AddInquiry*`
call throws before those callbacks run.

A test that never opens a connection still needs a value. The `AddInquiry*` call rejects a missing
or blank one. Some providers, such as PostgreSQL, also parse the value when they build the data
source, so it must be a valid connection string for that provider. The value does not need a
reachable server: registration builds the data source without opening a connection.

To use a container per test run, start it before the factory builds the host, for example in the
factory's `InitializeAsync` with xUnit's `IAsyncLifetime`, and pass its connection string to
`UseSetting`.

## Existing data sources

All provider registrations can use an existing data source:

```csharp
NpgsqlDataSource dataSource = /* configured and owned by the application */;
builder.Services.AddInquiryPostgreSql(dataSource);
```

To get the Aspire telemetry and health check for that registration, reference `Ignyte.Inquiry.Aspire`
and call `builder.AddInquiryAspireDefaults()`. The Aspire provider registrations call it for you, and a
second call adds nothing.

Inquiry opens pipeline connections from that data source and does not dispose it. The application
that creates the data source remains responsible for its lifetime. SQL Server accepts a
`DbDataSource`; `SqlClientFactory.CreateDataSource` creates the provider's internal implementation.
SQLite and Oracle accept the same generic `DbDataSource` contract.

For an externally configured `MySqlDataSource`, enable `AllowUserVariables` when generated GUID keys
use Inquiry's emulated `RETURNING` path.
