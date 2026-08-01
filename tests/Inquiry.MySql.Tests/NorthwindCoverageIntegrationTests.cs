using Inquiry;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Broader Northwind coverage for the MySQL provider — every classic table that the
/// shared <see cref="NorthwindCrudIntegrationTests"/> file does not already exercise.
/// Each fact runs in its own throwaway database so parallel facts cannot collide on
/// table state.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class NorthwindCoverageIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public NorthwindCoverageIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SupplierIdentityCrudRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "supplier");
        var store = harness.GetRequiredService<SupplierStore>();

        var inserted = await store.InsertReturningAsync(new Supplier
        {
            CompanyName = "Exotic Liquids",
            ContactName = "Charlotte Cooper",
            Country = "UK",
        });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.SupplierID);

        inserted.ContactName = "Updated";
        Assert.True(await store.UpdateAsync(inserted));

        var fetched = await store.SelectByKeyAsync(inserted.SupplierID);
        Assert.NotNull(fetched);
        Assert.Equal("Updated", fetched!.ContactName);

        Assert.True(await store.DeleteByKeyAsync(inserted.SupplierID));
        Assert.Null(await store.SelectByKeyAsync(inserted.SupplierID));
    }

    [SkippableFact]
    public async Task ShipperIdentityCrudRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "shipper");
        var store = harness.GetRequiredService<ShipperStore>();

        var inserted = await store.InsertReturningAsync(new Shipper { CompanyName = "Speedy Express", Phone = "(503) 555-9831" });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.ShipperID);

        var fetched = await store.SelectByKeyAsync(inserted.ShipperID);
        Assert.NotNull(fetched);
        Assert.Equal("Speedy Express", fetched!.CompanyName);

        Assert.True(await store.DeleteByKeyAsync(inserted.ShipperID));
    }

    [SkippableFact]
    public async Task OrderInsertReturningSurfacesGeneratedIdentity()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "order");
        var customers = harness.GetRequiredService<CustomerStore>();
        var orders = harness.GetRequiredService<OrderStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds" });

        var order = await orders.InsertReturningAsync(new Order
        {
            CustomerID = "ALFKI",
            OrderDate = new DateTime(1996, 7, 4),
            Freight = 32.38m,
        });

        Assert.NotNull(order);
        Assert.NotNull(order!.OrderID);
        Assert.True(order.OrderID > 0);

        var fetched = await orders.SelectByKeyAsync(order.OrderID);
        Assert.Equal(32.38m, fetched!.Freight);
    }

    [SkippableFact]
    public async Task OrderDetailCompositeKeyCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "orderdetail");
        var customers = harness.GetRequiredService<CustomerStore>();
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();
        var orders = harness.GetRequiredService<OrderStore>();
        var orderDetails = harness.GetRequiredService<OrderDetailStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds" });
        var beverages = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        var chai = await products.InsertReturningAsync(new Product { ProductName = "Chai",  CategoryID = beverages!.CategoryID, UnitPrice = 18m });
        var chang = await products.InsertReturningAsync(new Product { ProductName = "Chang", CategoryID = beverages.CategoryID,  UnitPrice = 19m });

        var order = await orders.InsertReturningAsync(new Order { CustomerID = "ALFKI" });
        Assert.NotNull(order);
        var orderID = order!.OrderID!.Value;

        await orderDetails.InsertAsync(new OrderDetail { OrderID = orderID, ProductID = chai!.ProductID!.Value,  UnitPrice = 18m, Quantity = 10, Discount = 0f   });
        await orderDetails.InsertAsync(new OrderDetail { OrderID = orderID, ProductID = chang!.ProductID!.Value, UnitPrice = 19m, Quantity =  5, Discount = 0.1f });

        var lines = await orderDetails.SelectByOrderAsync(orderID).ToListAsync();
        Assert.Equal(2, lines.Count);

        var line = await orderDetails.SelectByKeyAsync(orderID, chai.ProductID!.Value);
        Assert.NotNull(line);
        Assert.Equal(10, line!.Quantity);

        line.Quantity = 99;
        Assert.True(await orderDetails.UpdateAsync(line));
        Assert.Equal(99, (await orderDetails.SelectByKeyAsync(orderID, chai.ProductID!.Value))!.Quantity);

        Assert.True(await orderDetails.DeleteByKeyAsync(orderID, chai.ProductID!.Value));
        Assert.Null(await orderDetails.SelectByKeyAsync(orderID, chai.ProductID!.Value));
    }

    [SkippableFact]
    public async Task EmployeeTerritoryIntStringCompositeKeyRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "empterritory");
        var employees = harness.GetRequiredService<EmployeeStore>();
        var regions = harness.GetRequiredService<RegionStore>();
        var territories = harness.GetRequiredService<TerritoryStore>();
        var bridge = harness.GetRequiredService<EmployeeTerritoryStore>();

        var nancy = await employees.InsertReturningAsync(new Employee { FirstName = "Nancy", LastName = "Davolio" });
        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territories.InsertAsync(new Territory { TerritoryID = "01581", TerritoryDescription = "Westboro", RegionID = 1 });
        await territories.InsertAsync(new Territory { TerritoryID = "01730", TerritoryDescription = "Bedford",  RegionID = 1 });

        await bridge.InsertAsync(new EmployeeTerritory { EmployeeID = nancy!.EmployeeID!.Value, TerritoryID = "01581" });
        await bridge.InsertAsync(new EmployeeTerritory { EmployeeID = nancy.EmployeeID!.Value,  TerritoryID = "01730" });

        var byEmployee = await bridge.SelectByEmployeeAsync(nancy.EmployeeID!.Value).ToListAsync();
        Assert.Equal(2, byEmployee.Count);

        Assert.NotNull(await bridge.SelectByKeyAsync(nancy.EmployeeID!.Value, "01581"));
        Assert.True(await bridge.DeleteByKeyAsync(nancy.EmployeeID!.Value, "01581"));
        Assert.Null(await bridge.SelectByKeyAsync(nancy.EmployeeID!.Value, "01581"));
    }

    [SkippableFact]
    public async Task MultiFieldSelectFiltersByBothColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "multifield");
        var customers = harness.GetRequiredService<CustomerStore>();
        var employees = harness.GetRequiredService<EmployeeStore>();
        var orders = harness.GetRequiredService<OrderStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds" });
        await customers.InsertAsync(new Customer { CustomerID = "BONAP", CompanyName = "Bon app'" });
        var nancy = await employees.InsertReturningAsync(new Employee { FirstName = "Nancy", LastName = "Davolio" });
        var andrew = await employees.InsertReturningAsync(new Employee { FirstName = "Andrew", LastName = "Fuller" });

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = nancy!.EmployeeID,  ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = andrew!.EmployeeID, ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = nancy.EmployeeID,   ShipCity = "Marseille" });

        var matched = await orders.SelectByCustomerAndEmployeeAsync("ALFKI", nancy.EmployeeID).ToListAsync();
        var only = Assert.Single(matched);
        Assert.Equal("Berlin", only.ShipCity);
    }

    [SkippableFact]
    public async Task MutationReturningSurfacesUpdatedAndUpsertedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Customer { CustomerID = "UPD01", CompanyName = "Original", Country = "USA" });

        var updated = await store.UpdateReturningAsync(new Customer { CustomerID = "UPD01", CompanyName = "Updated", Country = "Canada" });
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.CompanyName);
        Assert.Equal("Canada", updated.Country);

        var missing = await store.UpdateReturningAsync(new Customer { CustomerID = "GONE1", CompanyName = "Missing" });
        Assert.Null(missing);

        var upserted = await store.UpsertReturningAsync(new Customer { CustomerID = "UPS01", CompanyName = "New", Country = "USA" });
        Assert.NotNull(upserted);
        Assert.Equal("New", upserted!.CompanyName);
    }

    /// <summary>
    /// Generated-key upsert-returning INSERT branch (<c>ProductStore.UpsertReturningAsync</c> with no
    /// key supplied) surfaces the freshly generated key — the emulated returning <c>SELECT</c> keyed off
    /// <c>LAST_INSERT_ID()</c> reads back the inserted row.
    /// </summary>
    [SkippableFact]
    public async Task ProductUpsertReturningInsertBranchSurfacesGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsertgenins");
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var beverages = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });

        var inserted = await products.UpsertReturningAsync(new Product
        {
            ProductName = "Chai",
            CategoryID = beverages!.CategoryID,
            UnitPrice = 18m,
            Discontinued = false,
        });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.ProductID);
        Assert.True(inserted.ProductID > 0);
        Assert.Equal("Chai", inserted.ProductName);

        // The inserted row is readable by key (independent of the returning path).
        var fetched = await products.SelectByKeyAsync(inserted.ProductID);
        Assert.NotNull(fetched);
        Assert.Equal(18m, fetched!.UnitPrice);
    }

    /// <summary>
    /// Generated-key upsert-returning over the <c>ON DUPLICATE KEY UPDATE</c> (update) branch surfaces the
    /// updated row. The upsert adds <c>key = LAST_INSERT_ID(key)</c> to the update set, so the emulated
    /// returning <c>SELECT</c> (keyed on <c>LAST_INSERT_ID()</c>) reads the existing row back even when the
    /// statement updates rather than inserts.
    /// </summary>
    [SkippableFact]
    public async Task ProductUpsertReturningUpdateBranchSurfacesUpdatedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "upsertgenupd");
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var beverages = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        var inserted = await products.UpsertReturningAsync(new Product { ProductName = "Chai", CategoryID = beverages!.CategoryID, UnitPrice = 18m, Discontinued = false });

        inserted!.UnitPrice = 99m;
        var updated = await products.UpsertReturningAsync(inserted);
        Assert.NotNull(updated);
        Assert.Equal(inserted.ProductID, updated!.ProductID);
        Assert.Equal(99m, updated.UnitPrice);
    }

    [SkippableFact]
    public async Task CategoryProductEagerLoadStitchesNullableForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "eagercat");
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var beverages = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        await products.InsertAsync(new Product { ProductName = "Chai",  CategoryID = beverages!.CategoryID });
        await products.InsertAsync(new Product { ProductName = "Chang", CategoryID = beverages.CategoryID });

        var loaded = await categories.SelectByKeyWithProductsAsync(beverages.CategoryID);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Products);
        Assert.Equal(2, loaded.Products!.Count);

        var withCategory = await products.SelectAllWithCategoryAsync().ToListAsync();
        Assert.All(withCategory, p => Assert.Equal("Beverages", p.Category?.CategoryName));
    }

    [SkippableFact]
    public async Task TransactionSpansIdentityParentAndCompositeChild()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "txorder");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var customers = harness.GetRequiredService<CustomerStore>();
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();
        var orders = harness.GetRequiredService<OrderStore>();
        var orderDetails = harness.GetRequiredService<OrderDetailStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds" });
        var beverages = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        var chai = await products.InsertReturningAsync(new Product { ProductName = "Chai", CategoryID = beverages!.CategoryID, UnitPrice = 18m });

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            var order = await orders.InsertReturningAsync(new Order { CustomerID = "ALFKI", Freight = 5m });
            await orderDetails.InsertAsync(new OrderDetail { OrderID = order!.OrderID!.Value, ProductID = chai!.ProductID!.Value, UnitPrice = 18m, Quantity = 2, Discount = 0f });
            // Dispose without commit — both rolled back.
        }

        Assert.Empty(await orders.SelectAllAsync().ToListAsync());
        Assert.Empty(await orderDetails.SelectAllAsync().ToListAsync());

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            var order = await orders.InsertReturningAsync(new Order { CustomerID = "ALFKI", Freight = 7m });
            await orderDetails.InsertAsync(new OrderDetail { OrderID = order!.OrderID!.Value, ProductID = chai!.ProductID!.Value, UnitPrice = 18m, Quantity = 3, Discount = 0f });
            await tx.CommitAsync();
        }

        var allOrders = await orders.SelectAllAsync().ToListAsync();
        var allDetails = await orderDetails.SelectAllAsync().ToListAsync();
        Assert.Single(allOrders);
        Assert.Single(allDetails);
        Assert.Equal(7m, allOrders[0].Freight);
        Assert.Equal(3, allDetails[0].Quantity);
    }

    [SkippableFact]
    public async Task EmployeeReportsToSelfReferenceRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "emp");
        var employees = harness.GetRequiredService<EmployeeStore>();

        var manager = await employees.InsertReturningAsync(new Employee { FirstName = "Andrew", LastName = "Fuller", Title = "VP" });
        var report = await employees.InsertReturningAsync(new Employee { FirstName = "Nancy", LastName = "Davolio", Title = "Sales", ReportsTo = manager!.EmployeeID });

        var fetched = await employees.SelectByKeyAsync(report!.EmployeeID);
        Assert.NotNull(fetched);
        Assert.Equal(manager.EmployeeID, fetched!.ReportsTo);
    }

    [SkippableFact]
    public async Task TerritoryByRegionSelectsOnlyMatchingRegion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "terrbyregion");
        var regions = harness.GetRequiredService<RegionStore>();
        var territories = harness.GetRequiredService<TerritoryStore>();

        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await regions.InsertAsync(new Region { RegionID = 2, RegionDescription = "Western" });

        await territories.InsertAsync(new Territory { TerritoryID = "E1", TerritoryDescription = "Eastern-1", RegionID = 1 });
        await territories.InsertAsync(new Territory { TerritoryID = "E2", TerritoryDescription = "Eastern-2", RegionID = 1 });
        await territories.InsertAsync(new Territory { TerritoryID = "W1", TerritoryDescription = "Western-1", RegionID = 2 });

        var eastern = await territories.SelectByRegionAsync(1).ToListAsync();
        Assert.Equal(2, eastern.Count);
        Assert.All(eastern, t => Assert.Equal(1, t.RegionID));
    }
}
