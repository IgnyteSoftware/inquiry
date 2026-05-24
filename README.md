# Inquiry

Inquiry is an experimental .NET 6+ source-generated micro-ORM. It maps entity classes with attributes and generates store implementations for common CRUD operations.

## Projects

- `src/Inquiry`: public attributes, connection abstractions, and store base types.
- `src/Inquiry.Generators`: Roslyn source generator for entity metadata, diagnostics, SQL, materializers, parameter binding, and generated stores.
- `src/Inquiry.SqlServer`: SQL Server connection factory using `Microsoft.Data.SqlClient`.
- `src/Inquiry.Sqlite`: SQLite connection factory using `Microsoft.Data.Sqlite`.
- `tests/Inquiry.Tests`: core API tests.
- `tests/Inquiry.Generators.Tests`: source-generator compilation tests.
- `tests/Inquiry.Sqlite.Tests`: generated-store integration tests against SQLite.
- `samples/Inquiry.Sample`: runnable SQLite and dependency-injection sample application.

## Current Store Shape

The initial generator keeps the product-document concept of user-authored abstract stores, but generates a concrete derived implementation because source generators cannot fill in abstract members on the same partial class.

```csharp
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
    protected OrganizationStore(IInquiry inquiry)
        : base(inquiry)
    {
    }

    [InquirySelect]
    public abstract IAsyncEnumerable<Organization> SelectAllAsync(CancellationToken cancellationToken = default);

    [InquirySelectByKey]
    public abstract Task<Organization?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Organization> SelectManuallyAsync(CancellationToken cancellationToken = default)
    {
        return _inquiry.QueryAsync<Organization>(
            "SELECT [Key], [Name], [IsActive] FROM [TOrganization]",
            cancellationToken);
    }
}
```

The generator emits `GeneratedOrganizationStore : OrganizationStore` and generated materializers for mapped entities.

## Dependency Injection

Inquiry is designed for applications that use `Microsoft.Extensions.DependencyInjection`. Provider packages register the database connection factory and core Inquiry services. Generated code registers each store and entity materializer.

```csharp
services
    .AddInquirySqlite(connectionString)
    .AddInquiryStores();

public sealed class OrganizationService
{
    private readonly OrganizationStore _organizations;

    public OrganizationService(OrganizationStore organizations)
    {
        _organizations = organizations;
    }
}
```

`AddInquiryStores()` is generated in the consuming project and registers each user-defined store, for example `OrganizationStore`, against its generated implementation.

## IInquiry

`IInquiry` is the simple query facade available inside stores as `_inquiry` and injectable into application services when needed.

```csharp
var organizations = _inquiry.QueryAsync<Organization>(
    "SELECT [Key], [Name], [IsActive] FROM [TOrganization]");

var organization = await _inquiry.QuerySingleOrDefaultAsync<Organization>(
    "SELECT [Key], [Name], [IsActive] FROM [TOrganization]");
```

Generated CRUD methods use `IInquiry` as well. `IInquiry` then delegates to the request pipeline so interceptors and provider behavior apply consistently.

## Request Pipeline

`IInquiryRequestPipeline` is the lower-level runtime layer beneath `IInquiry`. The pipeline owns ADO.NET connection, command, reader, and disposal behavior, while generated code supplies SQL, parameter binders, and materializers.

Most users should not need to inject the pipeline directly. Applications can register `IInquiryCommandInterceptor` implementations to observe commands, mutate command settings before execution, and receive success/failure callbacks. Interceptors cannot replace execution results.

## Sample Application

Run the sample application from the repository root:

```powershell
dotnet run --project samples\Inquiry.Sample\Inquiry.Sample.csproj
```

The sample uses SQLite, creates a local schema, registers Inquiry with dependency injection, resolves `OrganizationStore`, and runs generated CRUD methods.

## Supported Operations

- `[InquirySelect]`
- `[InquirySelectByKey]`
- `[InquirySelectByField]`
- `[InquiryInsert]`
- `[InquiryUpdate]`
- `[InquiryDeleteByKey]`

Version-one SQL generation currently uses square-bracket identifiers and `@` parameters. This works for SQL Server and SQLite; provider-specific SQL dialect generation is the next design step for PostgreSQL/MySQL and deeper provider customization.
