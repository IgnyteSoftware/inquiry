# Registering stores from multiple libraries

Give each data library one public registration method with a unique name. Call that method from the
host instead of calling `AddInquiryGeneratedStores()` there. Every generated assembly exposes that
same extension name, so a host referencing two of them can get CS0121 from an ambiguous call.

The library wrapper calls its own generated registration. This convention uses direct generated
calls, requires no reflection or `extern alias`, and works with trimming and NativeAOT.

## Data libraries own store registration

Put this file in the **Sales** project, alongside its entities and partial stores. That project
references its provider package, for example `Ignyte.Inquiry.Sqlite`.

```csharp
using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Sales;

public static class SalesRegistration
{
    public static IServiceCollection AddSalesData(this IServiceCollection services)
        => services.AddInquiryGeneratedStores();
}
```

In the separate **Inventory** project, use a different wrapper name:

```csharp
using Inquiry.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory;

public static class InventoryRegistration
{
    public static IServiceCollection AddInventoryData(this IServiceCollection services)
        => services.AddInquiryGeneratedStores();
}
```

These are application-owned methods, not Inquiry APIs. Each wrapper must compile in the assembly
that generates its stores. A wrapper in a third assembly would face the same ambiguity as the host.
Existing wrappers such as `AddNorthwindSampleStores` and `AddProductWorkspace` follow this convention.

## The host owns options and the connection

For a host referencing both libraries, configure the shared runtime and provider once:

```csharp
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inventory;
using Microsoft.Extensions.DependencyInjection;
using Sales;

var services = new ServiceCollection();
services.AddInquiry();
services.AddInquirySqlite("Data Source=application.db");
services.AddSalesData();
services.AddInventoryData();

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
// Resolve each library's store types from scope.ServiceProvider.
```

Both libraries must compile for the configured dialect and use the same database connection factory.
Multiple libraries do not imply multiple providers or connection strings. A second provider
registration is rejected, even if it names the same provider. Do not register one inside each wrapper.

In an ASP.NET Core host, the corresponding setup is:

```csharp
builder.Services.AddInquiry();
builder.Services.AddInquirySqlite(builder.Configuration, "Application");
builder.Services.AddSalesData();
builder.Services.AddInventoryData();
```

This reads `ConnectionStrings:Application`. Configure core options through the host's single
`AddInquiry(options => ...)` call when needed. The wrappers deliberately need no configuration
argument because the host owns the connection and options. If an application wants a single
`AddApplicationData(configuration)` entry point, its composition layer can contain these four calls.

Registration never creates the database or tables. Initialize the schema or run migrations explicitly
before the first query. Generated stores and `IInquiry` are scoped; resolve them inside an explicit
scope in console applications or use the request scope in ASP.NET Core.
