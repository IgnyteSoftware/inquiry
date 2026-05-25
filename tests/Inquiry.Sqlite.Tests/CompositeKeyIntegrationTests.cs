using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end CRUD against a composite-key entity: Northwind's <c>Order Details</c>
/// (PK = OrderID + ProductID). Each store method takes the key columns as positional
/// parameters in declaration order.
/// </summary>
public sealed class CompositeKeyIntegrationTests
{
    [Fact]
    public async Task SelectByKeyFindsRowMatchingBothKeyColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
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

    [Fact]
    public async Task SelectByKeyReturnsNullWhenEitherKeyDoesNotMatch()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });

        // First key wrong.
        Assert.Null(await store.SelectByKeyAsync(99, 1));
        // Second key wrong.
        Assert.Null(await store.SelectByKeyAsync(1, 99));
        // Both wrong.
        Assert.Null(await store.SelectByKeyAsync(99, 99));
    }

    [Fact]
    public async Task UpdatePersistsChangesIdentifiedByBothKeyColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
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

    [Fact]
    public async Task DeleteByKeyRemovesOnlyTheRowMatchingBothKeyColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 2, UnitPrice = 20m, Quantity = 3, Discount = 0f });

        var deleted = await store.DeleteByKeyAsync(1, 1);
        Assert.True(deleted);

        Assert.Null(await store.SelectByKeyAsync(1, 1));
        Assert.NotNull(await store.SelectByKeyAsync(1, 2));
    }

    [Fact]
    public async Task SelectAllByOrderReturnsAllLinesForThatOrder()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 2, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 2, UnitPrice = 20m, Quantity = 3, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 30m, Quantity = 1, Discount = 0f });

        var lines = await store.SelectByOrderAsync(1).ToListAsync();

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, d => d.ProductID == 1);
        Assert.Contains(lines, d => d.ProductID == 2);
    }

    [Fact]
    public async Task CompositeKeyAllowsSameSecondKeyAcrossDifferentFirstKeys()
    {
        // Sanity check that the PK is genuinely composite: (1, 1) and (2, 1) can coexist.
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
        var store = harness.GetRequiredService<OrderDetailStore>();

        await store.InsertAsync(new OrderDetail { OrderID = 1, ProductID = 1, UnitPrice = 10m, Quantity = 1, Discount = 0f });
        await store.InsertAsync(new OrderDetail { OrderID = 2, ProductID = 1, UnitPrice = 20m, Quantity = 1, Discount = 0f });

        var all = await store.SelectAllAsync().ToListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task StringPlusStringCompositeKeyRoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CompositeKey", foreignKeys: false);
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
