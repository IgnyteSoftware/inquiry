using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// End-to-end CRUD against composite-key entities on SQL Server. Uses a minimal DDL without
/// foreign keys so <c>[Order Details]</c> rows can be inserted without parent Orders/Products.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CompositeKeyIntegrationTests
{
    /// <summary>
    /// Minimal DDL for the four tables used by this test class, with no foreign key constraints.
    /// Column types mirror <see cref="Inquiry.Northwind.NorthwindSchema.SqlServerDdl"/>.
    /// </summary>
    private const string Ddl = """
        IF OBJECT_ID(N'Customers', N'U') IS NULL
        BEGIN
            CREATE TABLE Customers (
                CustomerID    NVARCHAR(5) NOT NULL PRIMARY KEY,
                CompanyName   NVARCHAR(40) NOT NULL,
                ContactName   NVARCHAR(MAX) NULL,
                ContactTitle  NVARCHAR(MAX) NULL,
                Address       NVARCHAR(MAX) NULL,
                City          NVARCHAR(60) NULL,
                Region        NVARCHAR(60) NULL,
                PostalCode    NVARCHAR(20) NULL,
                Country       NVARCHAR(MAX) NULL,
                Phone         NVARCHAR(MAX) NULL,
                Fax           NVARCHAR(MAX) NULL
            );
        END;

        IF OBJECT_ID(N'CustomerDemographics', N'U') IS NULL
        BEGIN
            CREATE TABLE CustomerDemographics (
                CustomerTypeID  NVARCHAR(10) NOT NULL PRIMARY KEY,
                CustomerDesc    NVARCHAR(MAX) NULL
            );
        END;

        IF OBJECT_ID(N'CustomerCustomerDemo', N'U') IS NULL
        BEGIN
            CREATE TABLE CustomerCustomerDemo (
                CustomerID      NVARCHAR(5)  NOT NULL,
                CustomerTypeID  NVARCHAR(10) NOT NULL,
                CONSTRAINT PK_CustomerCustomerDemo PRIMARY KEY (CustomerID, CustomerTypeID)
            );
        END;

        IF OBJECT_ID(N'Order Details', N'U') IS NULL
        BEGIN
            CREATE TABLE [Order Details] (
                OrderID    INT NOT NULL,
                ProductID  INT NOT NULL,
                UnitPrice  DECIMAL(19,4) NOT NULL DEFAULT 0,
                Quantity   SMALLINT      NOT NULL DEFAULT 1,
                Discount   REAL          NOT NULL DEFAULT 0,
                CONSTRAINT PK_Order_Details PRIMARY KEY (OrderID, ProductID)
            );
        END;
        """;

    private readonly SqlServerContainerFixture _fixture;
    public CompositeKeyIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectByKeyFindsRowMatchingBothKeyColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompSel");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 2, UnitPrice = 20m, Quantity = 3, Discount = 0.1f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 30m, Quantity = 1, Discount = 0f });

        var loaded = await store.SelectByKeyAsync(1, 2);

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.OrderID);
        Assert.Equal(2, loaded.ProductID);
        Assert.Equal(20m, loaded.UnitPrice);
        Assert.Equal(3, loaded.Quantity);
    }

    [SkippableFact]
    public async Task SelectByKeyReturnsNullWhenEitherKeyDoesNotMatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompNull");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });

        Assert.Null(await store.SelectByKeyAsync(99, 1));
        Assert.Null(await store.SelectByKeyAsync(1, 99));
        Assert.Null(await store.SelectByKeyAsync(99, 99));
    }

    [SkippableFact]
    public async Task UpdatePersistsChangesIdentifiedByBothKeyColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompUpd");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 30m, Quantity = 1, Discount = 0f });

        var updated = await store.UpdateAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 12m, Quantity = 5, Discount = 0.2f });

        Assert.True(updated);

        var changed = await store.SelectByKeyAsync(1, 1);
        Assert.Equal(12m, changed!.UnitPrice);
        Assert.Equal(5, changed.Quantity);
        Assert.Equal(0.2f, changed.Discount);

        var untouched = await store.SelectByKeyAsync(2, 1);
        Assert.Equal(30m, untouched!.UnitPrice);
        Assert.Equal(1, untouched.Quantity);
    }

    [SkippableFact]
    public async Task DeleteByKeyRemovesOnlyTheRowMatchingBothKeyColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompDel");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 2, UnitPrice = 20m, Quantity = 3, Discount = 0f });

        var deleted = await store.DeleteByKeyAsync(1, 1);
        Assert.True(deleted);

        Assert.Null(await store.SelectByKeyAsync(1, 1));
        Assert.NotNull(await store.SelectByKeyAsync(1, 2));
    }

    [SkippableFact]
    public async Task SelectAllByOrderReturnsAllLinesForThatOrder()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompOrder");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 2, UnitPrice = 20m, Quantity = 3, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 30m, Quantity = 1, Discount = 0f });

        var lines = await store.SelectByOrderAsync(1).ToListAsync();

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, d => d.ProductID == 1);
        Assert.Contains(lines, d => d.ProductID == 2);
    }

    [SkippableFact]
    public async Task CompositeKeyAllowsSameSecondKeyAcrossDifferentFirstKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompDup");
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 1, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 20m, Quantity = 1, Discount = 0f });

        var all = await store.SelectAllAsync().ToListAsync();
        Assert.Equal(2, all.Count);
    }

    [SkippableFact]
    public async Task StringPlusStringCompositeKeyRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "CompStr");
        var demoStore = harness.GetRequiredService<CustomerDemographicStore>();
        var bridgeStore = harness.GetRequiredService<CustomerCustomerDemoStore>();
        var customerStore = harness.GetRequiredService<CustomerStore>();

        await customerStore.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds Futterkiste" });
        await demoStore.InsertAsync(new CustomerDemographic { CustomerTypeID = "VIP", CustomerDesc = "Very Important" });
        await bridgeStore.InsertAsync(new CustomerCustomerDemo { CustomerID = "ALFKI", CustomerTypeID = "VIP" });

        var loaded = await bridgeStore.SelectByKeyAsync("ALFKI", "VIP");

        Assert.NotNull(loaded);
        Assert.Equal("ALFKI", loaded!.CustomerID);
        Assert.Equal("VIP", loaded.CustomerTypeID);

        var deleted = await bridgeStore.DeleteByKeyAsync("ALFKI", "VIP");
        Assert.True(deleted);
        Assert.Null(await bridgeStore.SelectByKeyAsync("ALFKI", "VIP"));
    }
}
