using System.Collections.Generic;
using Inquiry.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class SqliteProviderIntegrationTests
{
    [Fact]
    public void SqliteProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquirySqlLite("Data Source=:memory:")
            .BuildServiceProvider();

        Assert.IsType<SqliteInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.IsType<SqliteInquirySqlDialect>(serviceProvider.GetRequiredService<InquirySqlDialect>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }

    [Fact]
    public async Task GeneratedStoreExecutesCrudAgainstSqlite()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        using var serviceProvider = new ServiceCollection()
            .AddInquiry()
            .AddInquirySqlite(connectionString)
            .BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<OrganizationStore>();
        var key = Guid.NewGuid();
        var organization = new Organization
        {
            Key = key,
            Name = "Acme",
            IsActive = true,
        };

        var inserted = await store.InsertAsync(organization);
        var selected = await store.SelectByKeyAsync(key);
        var activeOrganizations = await ToListAsync(store.SelectByIsActiveAsync(true));
        var customQueriedOrganizations = await ToListAsync(store.SelectWithInquiryAsync());

        organization.Name = "Acme Updated";
        organization.IsActive = false;
        var updated = await store.UpdateAsync(organization);
        var selectedAfterUpdate = await store.SelectByKeyAsync(key);

        var deleted = await store.DeleteByKeyAsync(key);
        var selectedAfterDelete = await store.SelectByKeyAsync(key);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Acme", selected.Name);
        Assert.True(selected.IsActive);
        Assert.Single(activeOrganizations);
        Assert.Single(customQueriedOrganizations);
        Assert.Equal("Acme", customQueriedOrganizations[0].Name);
        Assert.True(updated);
        Assert.NotNull(selectedAfterUpdate);
        Assert.Equal("Acme Updated", selectedAfterUpdate.Name);
        Assert.False(selectedAfterUpdate.IsActive);
        Assert.True(deleted);
        Assert.Null(selectedAfterDelete);
    }

    private static string CreateSharedInMemoryConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "Inquiry_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };

        return builder.ToString();
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE TOrganization (
                [Key] TEXT PRIMARY KEY,
                [Name] TEXT NOT NULL,
                IsActive INTEGER DEFAULT 1 NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var results = new List<T>();
        await foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }
}
