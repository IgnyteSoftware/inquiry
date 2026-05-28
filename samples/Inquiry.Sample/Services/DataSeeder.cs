using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Populates every Northwind table with a small fixture so each Blazor page has something to
/// show. Invoked once from <c>Program.cs</c> during startup; no-ops if customers already exist.
/// </summary>
/// <remarks>
/// Seeds all 13 classic Northwind tables: Categories, Region, Territories, Suppliers,
/// Customers, CustomerDemographics, CustomerCustomerDemo, Employees, EmployeeTerritories,
/// Shippers, Products, Orders, Order Details. The data is intentionally minimal — enough rows
/// to exercise every generated store method (CRUD, eager-load, by-field, transactional
/// insert) without standing up the full historical fixture.
/// </remarks>
public sealed class DataSeeder
{
    private readonly CustomerStore _customers;
    private readonly EmployeeStore _employees;
    private readonly CategoryStore _categories;
    private readonly ProductStore _products;
    private readonly ShipperStore _shippers;
    private readonly SupplierStore _suppliers;
    private readonly RegionStore _regions;
    private readonly TerritoryStore _territories;
    private readonly EmployeeTerritoryStore _employeeTerritories;
    private readonly CustomerDemographicStore _demographics;
    private readonly CustomerCustomerDemoStore _customerDemographics;
    private readonly OrderStore _orders;
    private readonly OrderDetailStore _orderDetails;

    public DataSeeder(
        CustomerStore customers,
        EmployeeStore employees,
        CategoryStore categories,
        ProductStore products,
        ShipperStore shippers,
        SupplierStore suppliers,
        RegionStore regions,
        TerritoryStore territories,
        EmployeeTerritoryStore employeeTerritories,
        CustomerDemographicStore demographics,
        CustomerCustomerDemoStore customerDemographics,
        OrderStore orders,
        OrderDetailStore orderDetails)
    {
        _customers = customers;
        _employees = employees;
        _categories = categories;
        _products = products;
        _shippers = shippers;
        _suppliers = suppliers;
        _regions = regions;
        _territories = territories;
        _employeeTerritories = employeeTerritories;
        _demographics = demographics;
        _customerDemographics = customerDemographics;
        _orders = orders;
        _orderDetails = orderDetails;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingCustomers = await _customers.SelectAllAsync(cancellationToken).ConfigureAwait(false);
        if (existingCustomers.Count > 0)
        {
            return;
        }

        // Customers
        foreach (var c in new[]
        {
            new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds Futterkiste",     ContactName = "Maria Anders",     ContactTitle = "Sales Representative", Country = "Germany", City = "Berlin",    Phone = "030-0074321" },
            new Customer { CustomerID = "BLAUS", CompanyName = "Blauer See Delikatessen", ContactName = "Hanna Moos",       ContactTitle = "Sales Representative", Country = "Germany", City = "Mannheim",  Phone = "0621-08460"  },
            new Customer { CustomerID = "BONAP", CompanyName = "Bon app'",                ContactName = "Laurence Lebihan", ContactTitle = "Owner",                Country = "France",  City = "Marseille", Phone = "91.24.45.40" },
            new Customer { CustomerID = "EASTC", CompanyName = "Eastern Connection",      ContactName = "Ann Devon",        ContactTitle = "Sales Agent",          Country = "UK",      City = "London",    Phone = "(171) 555-0297" },
            new Customer { CustomerID = "FRANK", CompanyName = "Frankenversand",          ContactName = "Peter Franken",    ContactTitle = "Marketing Manager",    Country = "Germany", City = "München",   Phone = "089-0877310" },
        })
        {
            await _customers.InsertAsync(c, cancellationToken).ConfigureAwait(false);
        }

        // Employees — InsertReturning to capture IDENTITY-assigned EmployeeID for the
        // reports-to chain and the EmployeeTerritories join below.
        var nancy   = await _employees.InsertReturningAsync(new Employee { FirstName = "Nancy",    LastName = "Davolio",   Title = "Sales Representative",  HireDate = new DateTime(1992, 5,  1) }, cancellationToken).ConfigureAwait(false);
        var andrew  = await _employees.InsertReturningAsync(new Employee { FirstName = "Andrew",   LastName = "Fuller",    Title = "Vice President, Sales", HireDate = new DateTime(1992, 8, 14) }, cancellationToken).ConfigureAwait(false);
        var janet   = await _employees.InsertReturningAsync(new Employee { FirstName = "Janet",    LastName = "Leverling", Title = "Sales Representative",  HireDate = new DateTime(1992, 4,  1) }, cancellationToken).ConfigureAwait(false);
        var margaret = await _employees.InsertReturningAsync(new Employee { FirstName = "Margaret", LastName = "Peacock",   Title = "Sales Representative",  HireDate = new DateTime(1993, 5,  3) }, cancellationToken).ConfigureAwait(false);

        // Nancy, Janet, and Margaret report to Andrew.
        if (andrew?.EmployeeID is int andrewID)
        {
            foreach (var report in new[] { nancy, janet, margaret })
            {
                if (report is null) continue;
                report.ReportsTo = andrewID;
                await _employees.UpdateAsync(report, cancellationToken).ConfigureAwait(false);
            }
        }

        // Shippers
        foreach (var s in new[]
        {
            new Shipper { CompanyName = "Speedy Express",    Phone = "(503) 555-9831" },
            new Shipper { CompanyName = "United Package",    Phone = "(503) 555-3199" },
            new Shipper { CompanyName = "Federal Shipping",  Phone = "(503) 555-9931" },
        })
        {
            await _shippers.InsertAsync(s, cancellationToken).ConfigureAwait(false);
        }

        // Suppliers — IDENTITY-keyed; capture the assigned IDs for product wiring.
        var exoticLiquids       = await _suppliers.InsertReturningAsync(new Supplier { CompanyName = "Exotic Liquids",          ContactName = "Charlotte Cooper", Country = "UK",     City = "London"   }, cancellationToken).ConfigureAwait(false);
        var newOrleansCajun     = await _suppliers.InsertReturningAsync(new Supplier { CompanyName = "New Orleans Cajun Delights", ContactName = "Shelley Burke", Country = "USA",    City = "New Orleans" }, cancellationToken).ConfigureAwait(false);
        var grandmaKellysHomstead = await _suppliers.InsertReturningAsync(new Supplier { CompanyName = "Grandma Kelly's Homestead", ContactName = "Regina Murphy", Country = "USA",   City = "Ann Arbor" }, cancellationToken).ConfigureAwait(false);

        // Categories — InsertReturning so we capture the IDENTITY-assigned CategoryID.
        var beverages  = await _categories.InsertReturningAsync(new Category { CategoryName = "Beverages",      Description = "Soft drinks, coffees, teas, beers, and ales" },                cancellationToken).ConfigureAwait(false);
        var condiments = await _categories.InsertReturningAsync(new Category { CategoryName = "Condiments",     Description = "Sweet and savory sauces, relishes, spreads, and seasonings" }, cancellationToken).ConfigureAwait(false);
        var produce    = await _categories.InsertReturningAsync(new Category { CategoryName = "Produce",        Description = "Dried fruit and bean curd" },                                  cancellationToken).ConfigureAwait(false);
        var seafood    = await _categories.InsertReturningAsync(new Category { CategoryName = "Seafood",        Description = "Seaweed and fish" },                                           cancellationToken).ConfigureAwait(false);

        // Products — InsertReturning so we capture IDs for the order-details rows below.
        var chai      = await _products.InsertReturningAsync(new Product { ProductName = "Chai",                          SupplierID = exoticLiquids?.SupplierID,   CategoryID = beverages?.CategoryID,  UnitPrice = 18m,    UnitsInStock = 39, ReorderLevel = 10 }, cancellationToken).ConfigureAwait(false);
        var chang     = await _products.InsertReturningAsync(new Product { ProductName = "Chang",                         SupplierID = exoticLiquids?.SupplierID,   CategoryID = beverages?.CategoryID,  UnitPrice = 19m,    UnitsInStock = 17, ReorderLevel = 25 }, cancellationToken).ConfigureAwait(false);
        var aniseed   = await _products.InsertReturningAsync(new Product { ProductName = "Aniseed Syrup",                 SupplierID = exoticLiquids?.SupplierID,   CategoryID = condiments?.CategoryID, UnitPrice = 10m,    UnitsInStock = 13, ReorderLevel = 25 }, cancellationToken).ConfigureAwait(false);
        var cajun     = await _products.InsertReturningAsync(new Product { ProductName = "Chef Anton's Cajun Seasoning",  SupplierID = newOrleansCajun?.SupplierID, CategoryID = condiments?.CategoryID, UnitPrice = 22m,    UnitsInStock = 53, ReorderLevel = 0  }, cancellationToken).ConfigureAwait(false);
        var tofu      = await _products.InsertReturningAsync(new Product { ProductName = "Tofu",                          SupplierID = grandmaKellysHomstead?.SupplierID, CategoryID = produce?.CategoryID,    UnitPrice = 23.25m, UnitsInStock = 35, ReorderLevel = 0  }, cancellationToken).ConfigureAwait(false);
        var konbu     = await _products.InsertReturningAsync(new Product { ProductName = "Konbu",                         SupplierID = grandmaKellysHomstead?.SupplierID, CategoryID = seafood?.CategoryID,    UnitPrice = 6m,     UnitsInStock = 24, ReorderLevel = 5  }, cancellationToken).ConfigureAwait(false);

        // Region + Territories — Region uses a non-generated INT key.
        await _regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" },  cancellationToken).ConfigureAwait(false);
        await _regions.InsertAsync(new Region { RegionID = 2, RegionDescription = "Western" },  cancellationToken).ConfigureAwait(false);
        await _regions.InsertAsync(new Region { RegionID = 3, RegionDescription = "Northern" }, cancellationToken).ConfigureAwait(false);
        await _regions.InsertAsync(new Region { RegionID = 4, RegionDescription = "Southern" }, cancellationToken).ConfigureAwait(false);

        foreach (var t in new[]
        {
            new Territory { TerritoryID = "01581", TerritoryDescription = "Westboro",       RegionID = 1 },
            new Territory { TerritoryID = "01730", TerritoryDescription = "Bedford",        RegionID = 1 },
            new Territory { TerritoryID = "02116", TerritoryDescription = "Boston",         RegionID = 1 },
            new Territory { TerritoryID = "94025", TerritoryDescription = "Menlo Park",     RegionID = 2 },
            new Territory { TerritoryID = "98033", TerritoryDescription = "Kirkland",       RegionID = 2 },
            new Territory { TerritoryID = "55113", TerritoryDescription = "Roseville",      RegionID = 3 },
            new Territory { TerritoryID = "33607", TerritoryDescription = "Tampa",          RegionID = 4 },
        })
        {
            await _territories.InsertAsync(t, cancellationToken).ConfigureAwait(false);
        }

        // EmployeeTerritories — composite-key bridge. Nancy + Janet cover the East,
        // Margaret covers a Western territory.
        if (nancy?.EmployeeID is int nancyID)
        {
            await _employeeTerritories.InsertAsync(new EmployeeTerritory { EmployeeID = nancyID,   TerritoryID = "01581" }, cancellationToken).ConfigureAwait(false);
            await _employeeTerritories.InsertAsync(new EmployeeTerritory { EmployeeID = nancyID,   TerritoryID = "01730" }, cancellationToken).ConfigureAwait(false);
        }
        if (janet?.EmployeeID is int janetID)
        {
            await _employeeTerritories.InsertAsync(new EmployeeTerritory { EmployeeID = janetID,   TerritoryID = "02116" }, cancellationToken).ConfigureAwait(false);
        }
        if (margaret?.EmployeeID is int margaretID)
        {
            await _employeeTerritories.InsertAsync(new EmployeeTerritory { EmployeeID = margaretID, TerritoryID = "94025" }, cancellationToken).ConfigureAwait(false);
            await _employeeTerritories.InsertAsync(new EmployeeTerritory { EmployeeID = margaretID, TerritoryID = "98033" }, cancellationToken).ConfigureAwait(false);
        }

        // CustomerDemographics + bridge.
        await _demographics.InsertAsync(new CustomerDemographic { CustomerTypeID = "VIP",     CustomerDesc = "Very Important Customer" },     cancellationToken).ConfigureAwait(false);
        await _demographics.InsertAsync(new CustomerDemographic { CustomerTypeID = "REGULAR", CustomerDesc = "Standard ordering customer" },  cancellationToken).ConfigureAwait(false);
        await _customerDemographics.InsertAsync(new CustomerCustomerDemo { CustomerID = "ALFKI", CustomerTypeID = "VIP"     }, cancellationToken).ConfigureAwait(false);
        await _customerDemographics.InsertAsync(new CustomerCustomerDemo { CustomerID = "BONAP", CustomerTypeID = "REGULAR" }, cancellationToken).ConfigureAwait(false);

        // Orders + Order Details — round-trip the IDENTITY-keyed Orders row through
        // InsertReturning so we can reference the new OrderID from the composite-key detail
        // rows.
        var firstOrder = await _orders.InsertReturningAsync(new Order
        {
            CustomerID = "ALFKI",
            EmployeeID = nancy?.EmployeeID,
            OrderDate  = new DateTime(1996, 7, 4),
            RequiredDate = new DateTime(1996, 8, 1),
            ShippedDate  = new DateTime(1996, 7, 16),
            ShipVia    = 3,
            Freight    = 32.38m,
            ShipName   = "Alfreds Futterkiste",
            ShipCity   = "Berlin",
            ShipCountry = "Germany",
        }, cancellationToken).ConfigureAwait(false);

        var secondOrder = await _orders.InsertReturningAsync(new Order
        {
            CustomerID = "BONAP",
            EmployeeID = janet?.EmployeeID,
            OrderDate  = new DateTime(1996, 7, 5),
            RequiredDate = new DateTime(1996, 8, 2),
            ShipVia    = 1,
            Freight    = 11.61m,
            ShipName   = "Bon app'",
            ShipCity   = "Marseille",
            ShipCountry = "France",
        }, cancellationToken).ConfigureAwait(false);

        if (firstOrder?.OrderID is int firstOrderID)
        {
            if (chai?.ProductID  is int p1) await _orderDetails.InsertAsync(new OrderDetail { OrderID = firstOrderID, ProductID = p1, UnitPrice = chai!.UnitPrice  ?? 0m, Quantity = 10, Discount = 0f   }, cancellationToken).ConfigureAwait(false);
            if (chang?.ProductID is int p2) await _orderDetails.InsertAsync(new OrderDetail { OrderID = firstOrderID, ProductID = p2, UnitPrice = chang!.UnitPrice ?? 0m, Quantity = 5,  Discount = 0.1f }, cancellationToken).ConfigureAwait(false);
        }
        if (secondOrder?.OrderID is int secondOrderID)
        {
            if (tofu?.ProductID  is int p3) await _orderDetails.InsertAsync(new OrderDetail { OrderID = secondOrderID, ProductID = p3, UnitPrice = tofu!.UnitPrice  ?? 0m, Quantity = 3,  Discount = 0f   }, cancellationToken).ConfigureAwait(false);
            if (konbu?.ProductID is int p4) await _orderDetails.InsertAsync(new OrderDetail { OrderID = secondOrderID, ProductID = p4, UnitPrice = konbu!.UnitPrice ?? 0m, Quantity = 12, Discount = 0f   }, cancellationToken).ConfigureAwait(false);
        }
    }

}
