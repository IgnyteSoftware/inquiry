using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// End-to-end test of multi-field select on SQL Server. Uses a minimal DDL without foreign keys
/// so Orders can be inserted without parent Customers/Employees/Shippers.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MultiFieldSelectIntegrationTests
{
    /// <summary>
    /// Minimal Orders table DDL with no foreign key constraints.
    /// Column types mirror <see cref="Inquiry.Northwind.NorthwindSchema.SqlServerDdl"/>.
    /// </summary>
    private const string Ddl = """
        IF OBJECT_ID(N'Orders', N'U') IS NULL
        BEGIN
            CREATE TABLE Orders (
                OrderID         INT IDENTITY(1,1) PRIMARY KEY,
                CustomerID      NVARCHAR(5) NULL,
                EmployeeID      INT NULL,
                OrderDate       DATETIME NULL,
                RequiredDate    DATETIME NULL,
                ShippedDate     DATETIME NULL,
                ShipVia         INT NULL,
                Freight         DECIMAL(19,4) NULL DEFAULT 0,
                ShipName        NVARCHAR(MAX) NULL,
                ShipAddress     NVARCHAR(MAX) NULL,
                ShipCity        NVARCHAR(MAX) NULL,
                ShipRegion      NVARCHAR(MAX) NULL,
                ShipPostalCode  NVARCHAR(20) NULL,
                ShipCountry     NVARCHAR(MAX) NULL
            );
        END;
        """;

    private readonly SqlServerContainerFixture _fixture;
    public MultiFieldSelectIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectByTwoFieldsReturnsOnlyRowsMatchingBoth()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "MfBoth");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "MfEmpty");
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
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "MfSingle");
        var orders = harness.GetRequiredService<OrderStore>();

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 1 });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = 2 });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = 3 });

        var alfkiOrders = await orders.SelectByCustomerAsync("ALFKI").ToListAsync();

        Assert.Equal(2, alfkiOrders.Count);
        Assert.All(alfkiOrders, o => Assert.Equal("ALFKI", o.CustomerID));
    }
}
