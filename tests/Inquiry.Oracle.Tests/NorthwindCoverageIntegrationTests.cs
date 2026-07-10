using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Broader Northwind coverage for the Oracle provider — every classic table that the
/// shared <see cref="NorthwindCrudIntegrationTests"/> file does not already exercise.
/// Each fact runs in its own throwaway schema so parallel facts cannot collide on
/// table state.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class NorthwindCoverageIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public NorthwindCoverageIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SupplierIdentityCrudRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "supplier");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "shipper");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "order");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "orderdetail");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "empterritory");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "multifield");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "returning");
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

    [SkippableFact]
    public async Task CategoryProductEagerLoadStitchesNullableForeignKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "eagercat");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "txorder");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "emp");
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
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "terrbyregion");
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
