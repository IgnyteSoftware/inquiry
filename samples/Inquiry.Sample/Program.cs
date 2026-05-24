using Inquiry;
using Inquiry.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

var databasePath = Path.Combine(AppContext.BaseDirectory, "inquiry-sample.db");
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
}.ToString();

await SampleDatabase.CreateSchemaAsync(connectionString);

using var services = new ServiceCollection()
    .AddInquirySqlite(connectionString)
    .AddInquiryStores()
    .AddTransient<OrganizationWorkflow>()
    .BuildServiceProvider();

var workflow = services.GetRequiredService<OrganizationWorkflow>();
await workflow.RunAsync();

public sealed class OrganizationWorkflow
{
    private readonly OrganizationStore _organizations;

    public OrganizationWorkflow(OrganizationStore organizations)
    {
        _organizations = organizations;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var organization = new Organization
        {
            Key = Guid.NewGuid(),
            Name = "Acme Research",
            IsActive = true,
        };

        await _organizations.InsertAsync(organization, cancellationToken);

        var selected = await _organizations.SelectByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine($"Inserted: {selected?.Name} ({selected?.Key})");

        organization.Name = "Acme Research Group";
        organization.IsActive = false;
        await _organizations.UpdateAsync(organization, cancellationToken);

        Console.WriteLine("Active organizations:");
        await foreach (var activeOrganization in _organizations.SelectByIsActiveAsync(true, cancellationToken))
        {
            Console.WriteLine($"- {activeOrganization.Name}");
        }

        var updated = await _organizations.SelectByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine($"Updated: {updated?.Name}, active={updated?.IsActive}");

        Console.WriteLine("All organizations through IInquiry:");
        await foreach (var queriedOrganization in _organizations.SelectAllWithInquiryAsync(cancellationToken))
        {
            Console.WriteLine($"- {queriedOrganization.Name}");
        }

        await _organizations.DeleteByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine("Deleted sample organization.");
    }
}

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

    [InquirySelectByField("IsActive")]
    public abstract IAsyncEnumerable<Organization> SelectByIsActiveAsync(bool isActive, CancellationToken cancellationToken = default);

    [InquiryInsert]
    public abstract Task<int> InsertAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public abstract Task<bool> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

    [InquiryDeleteByKey]
    public abstract Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<Organization> SelectAllWithInquiryAsync(CancellationToken cancellationToken = default)
    {
        return _inquiry.QueryAsync<Organization>("SELECT [Key], [Name], [IsActive] FROM [TOrganization]", cancellationToken);
    }
}

internal static class SampleDatabase
{
    public static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS TOrganization (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1 NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }
}
