using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
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
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl);
        var store = harness.GetRequiredService<CustomerStore>();
        var customer = new Customer
        {
            CustomerID = "ACME1",
            CompanyName = "Acme Research",
            Country = "USA",
        };

        var inserted = await store.InsertAsync(customer);
        var selected = await store.SelectByKeyAsync("ACME1");
        var usCustomers = await store.SelectByCountryAsync("USA");

        customer.CompanyName = "Acme Updated";
        customer.Country = "Canada";
        var updated = await store.UpdateAsync(customer);
        var selectedAfterUpdate = await store.SelectByKeyAsync("ACME1");

        var deleted = await store.DeleteByKeyAsync("ACME1");
        var selectedAfterDelete = await store.SelectByKeyAsync("ACME1");

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Acme Research", selected.CompanyName);
        Assert.Equal("USA", selected.Country);
        Assert.Single(usCustomers);
        Assert.True(updated);
        Assert.NotNull(selectedAfterUpdate);
        Assert.Equal("Acme Updated", selectedAfterUpdate.CompanyName);
        Assert.Equal("Canada", selectedAfterUpdate.Country);
        Assert.True(deleted);
        Assert.Null(selectedAfterDelete);
    }
}
