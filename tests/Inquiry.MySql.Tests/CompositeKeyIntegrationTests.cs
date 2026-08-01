using Inquiry.MySql.Tests.Fixtures;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.MySql.Tests;

/// <summary>
/// End-to-end CRUD against composite-key entities on MySQL. Uses a minimal DDL without
/// foreign keys so <c>`Order Details`</c> rows can be inserted without parent Orders/Products.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class CompositeKeyIntegrationTests
{
    /// <summary>
    /// Minimal DDL for the four tables used by this test class, with no foreign key constraints.
    /// Column types mirror <see cref="Inquiry.Northwind.NorthwindSchema.MySqlDdl"/>.
    /// </summary>
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS `Customers` (
            `CustomerID`    VARCHAR(5) PRIMARY KEY NOT NULL,
            `CompanyName`   VARCHAR(40) NOT NULL,
            `ContactName`   LONGTEXT,
            `ContactTitle`  LONGTEXT,
            `Address`       LONGTEXT,
            `City`          LONGTEXT,
            `Region`        LONGTEXT,
            `PostalCode`    LONGTEXT,
            `Country`       LONGTEXT,
            `Phone`         LONGTEXT,
            `Fax`           LONGTEXT
        );

        CREATE TABLE IF NOT EXISTS `CustomerDemographics` (
            `CustomerTypeID`  VARCHAR(10) PRIMARY KEY NOT NULL,
            `CustomerDesc`    LONGTEXT
        );

        CREATE TABLE IF NOT EXISTS `CustomerCustomerDemo` (
            `CustomerID`      VARCHAR(5)  NOT NULL,
            `CustomerTypeID`  VARCHAR(10) NOT NULL,
            PRIMARY KEY (`CustomerID`, `CustomerTypeID`)
        );

        CREATE TABLE IF NOT EXISTS `Order Details` (
            `OrderID`    INT NOT NULL,
            `ProductID`  INT NOT NULL,
            `UnitPrice`  DECIMAL(19,4) NOT NULL DEFAULT 0,
            `Quantity`   SMALLINT      NOT NULL DEFAULT 1,
            `Discount`   FLOAT         NOT NULL DEFAULT 0,
            PRIMARY KEY (`OrderID`, `ProductID`)
        );
        """;

    private readonly MySqlContainerFixture _fixture;
    public CompositeKeyIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectByKeyFindsRowMatchingBothKeyColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_sel");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_null");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_upd");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_del");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_order");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_dup");
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
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "comp_str");
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
