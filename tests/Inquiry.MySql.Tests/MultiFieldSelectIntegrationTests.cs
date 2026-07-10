using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end test of multi-field select on MySQL. Uses a minimal DDL without foreign keys
/// so Orders can be inserted without parent Customers/Employees/Shippers.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class MultiFieldSelectIntegrationTests
{
    /// <summary>
    /// Minimal Orders table DDL with no foreign key constraints.
    /// Column types mirror <see cref="Inquiry.Northwind.NorthwindSchema.MySqlDdl"/>.
    /// </summary>
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS `Orders` (
            `OrderID`         INT AUTO_INCREMENT PRIMARY KEY,
            `CustomerID`      VARCHAR(5),
            `EmployeeID`      INT,
            `OrderDate`       DATETIME,
            `RequiredDate`    DATETIME,
            `ShippedDate`     DATETIME,
            `ShipVia`         INT,
            `Freight`         DECIMAL(19,4) DEFAULT 0,
            `ShipName`        LONGTEXT,
            `ShipAddress`     LONGTEXT,
            `ShipCity`        LONGTEXT,
            `ShipRegion`      LONGTEXT,
            `ShipPostalCode`  LONGTEXT,
            `ShipCountry`     LONGTEXT
        );
        """;

    private readonly MySqlContainerFixture _fixture;
    public MultiFieldSelectIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectByTwoFieldsReturnsOnlyRowsMatchingBoth()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "mf_both");
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

    [SkippableFact]
    public async Task SelectByTwoFieldsReturnsEmptyWhenSecondFieldDoesNotMatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "mf_empty");
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1 });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2 });

        var none = await orders.SelectByCustomerAndEmployeeAsync("ALFKI", 99).ToListAsync();

        Assert.Empty(none);
    }

    [SkippableFact]
    public async Task SingleColumnSelectByFieldStillWorks()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "mf_single");
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1 });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2 });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = 3 });

        var alfkiOrders = await orders.SelectByCustomerAsync("ALFKI").ToListAsync();

        Assert.Equal(2, alfkiOrders.Count);
        Assert.All(alfkiOrders, o => Assert.Equal("ALFKI", o.CustomerID));
    }
}
