# Inquiry

Inquiry is an experimental .NET 6+ source-generated micro-ORM. It maps entity classes with attributes and generates store implementations for common CRUD operations.

## Projects

- `src/Inquiry`: public attributes, connection abstractions, and store base types.
- `src/Inquiry.Generators`: Roslyn source generator for entity metadata, diagnostics, SQL, materializers, parameter binding, and generated stores.
- `src/Inquiry.SqlServer`: SQL Server connection factory and SQL dialect using `Microsoft.Data.SqlClient`.
- `src/Inquiry.Sqlite`: SQLite connection factory and SQL dialect using `Microsoft.Data.Sqlite`.
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

Inquiry is designed for applications that use `Microsoft.Extensions.DependencyInjection`. `AddInquiry()` registers the core runtime and discovers generated store/materializer registrations. Provider packages register only database-specific services such as connection factories.

```csharp
services
    .AddInquiry()
    .AddInquirySqlite(connectionString);

public sealed class OrganizationService
{
    private readonly OrganizationStore _organizations;

    public OrganizationService(OrganizationStore organizations)
    {
        _organizations = organizations;
    }
}
```

`AddInquiry()` registers the core runtime and discovers generated service registrations for user-defined stores, for example `OrganizationStore`, and generated entity materializers.

## IInquiry

`IInquiry` is the simple query facade available inside stores as `_inquiry` and injectable into application services when needed.

```csharp
var organizations = _inquiry.QueryAsync<Organization>(
    "SELECT [Key], [Name], [IsActive] FROM [TOrganization]");

var organization = await _inquiry.QuerySingleOrDefaultAsync<Organization>(
    "SELECT [Key], [Name], [IsActive] FROM [TOrganization]");
```

Queries and commands can take parameters with an anonymous object, a dictionary, or explicit `InquiryParameter` values when database metadata is needed.

```csharp
var activeOrganizations = _inquiry.QueryAsync<Organization>(
    "SELECT [Key], [Name], [IsActive] FROM [TOrganization] WHERE [IsActive] = @IsActive",
    new { IsActive = true });

await _inquiry.ExecuteAsync(
    "UPDATE [TOrganization] SET [Name] = @Name WHERE [Key] = @Key",
    new Dictionary<string, object?>
    {
        ["Key"] = organization.Key,
        ["Name"] = "Acme Research Group",
    });

await _inquiry.ExecuteAsync(
    "UPDATE [TOrganization] SET [Name] = @Name WHERE [Key] = @Key",
    new[]
    {
        new InquiryParameter("Key", organization.Key),
        new InquiryParameter("Name", "Acme Research Group", DbType.String, size: 200),
    });
```

Parameter names can include the provider prefix (`@Name`, `:Name`, `$Name`) or omit it. Unprefixed names are bound with an `@` prefix.

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

Generated stores build SQL through the `InquirySqlDialect` registered by the active provider package. Provider packages own provider-specific identifier quoting and parameter naming, while the core runtime owns the shared CRUD statement builder.
