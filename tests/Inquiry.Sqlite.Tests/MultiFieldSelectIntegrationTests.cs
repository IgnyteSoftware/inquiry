using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end test of <c>[InquirySelectAllByField("col1", "col2")]</c>. Filters Orders by the
/// AND of CustomerID and EmployeeID, mirroring how a multi-column filter would be used
/// against a parent/child link table or a multi-tenant scope.
/// </summary>
public sealed class MultiFieldSelectIntegrationTests
{
    [Fact]
    public async Task SelectByTwoFieldsReturnsOnlyRowsMatchingBoth()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "MultiField", foreignKeys: false);
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1, ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2, ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = 1, ShipCity = "Marseille" });

        var matched = await orders.SelectByCustomerAndEmployeeAsync("ALFKI", 1).ToListAsync();

        var only = Assert.Single(matched);
        Assert.Equal("ALFKI", only.CustomerID);
        Assert.Equal(1, only.EmployeeID);
        Assert.Equal("Berlin", only.ShipCity);
    }

    [Fact]
    public async Task SelectByTwoFieldsReturnsEmptyWhenSecondFieldDoesNotMatch()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "MultiField", foreignKeys: false);
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1 });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2 });

        var none = await orders.SelectByCustomerAndEmployeeAsync("ALFKI", 99).ToListAsync();

        Assert.Empty(none);
    }

    [Fact]
    public async Task SingleColumnSelectByFieldStillWorks()
    {
        // Belt-and-suspenders check that single-column SelectByField didn't regress when the
        // multi-column overload landed.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "MultiField", foreignKeys: false);
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1 });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2 });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = 3 });

        var alfkiOrders = await orders.SelectByCustomerAsync("ALFKI").ToListAsync();

        Assert.Equal(2, alfkiOrders.Count);
        Assert.All(alfkiOrders, o => Assert.Equal("ALFKI", o.CustomerID));
    }
}
