using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Pipeline;
using Inquiry.Sql;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class SqliteProviderIntegrationTests
{
    [Fact]
    public void SqliteProviderRegistersOnlyProviderServices()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInquirySqlite("Data Source=:memory:")
            .BuildServiceProvider();

        Assert.IsType<SqliteInquiryConnectionFactory>(serviceProvider.GetRequiredService<IInquiryConnectionFactory>());
        Assert.IsType<SqliteInquirySqlDialect>(serviceProvider.GetRequiredService<InquirySqlDialect>());
        Assert.Null(serviceProvider.GetService<IInquiry>());
        Assert.Null(serviceProvider.GetService<IInquiryRequestPipeline>());
    }

    [Fact]
    public async Task GeneratedStoreExecutesCrudAgainstSqlite()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.Organization);
        var store = harness.GetRequiredService<OrganizationStore>();
        var key = Guid.NewGuid();
        var organization = new Organization
        {
            Key = key,
            Name = "Acme",
            IsActive = true,
        };

        var inserted = await store.InsertAsync(organization);
        var selected = await store.SelectByKeyAsync(key);
        var activeOrganizations = await store.SelectByIsActiveAsync(true).ToListAsync();
        var customQueriedOrganizations = await store.SelectWithInquiryAsync().ToListAsync();

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
}
