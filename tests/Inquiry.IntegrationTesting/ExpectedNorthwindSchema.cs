namespace Inquiry.IntegrationTesting;

/// <summary>The single source of truth for what a faithful Northwind schema must contain.
/// Identifier comparison is case-insensitive, so engine casing differences do not matter.</summary>
public static class ExpectedNorthwindSchema
{
    private static ColumnSnapshot N(string name) => new(name, true);   // nullable
    private static ColumnSnapshot R(string name) => new(name, false);  // required (NOT NULL)
    private static ForeignKeySnapshot Fk(string col, string refTable, string refCol)
        => new(new[] { col }, refTable, new[] { refCol });
    private static IndexSnapshot Ix(params string[] cols) => new(cols);

    public static readonly SchemaSnapshot Schema = new(new[]
    {
        new TableSnapshot("Categories",
            new[] { R("CategoryID"), R("CategoryName"), N("Description"), N("Picture") },
            new[] { "CategoryID" },
            Array.Empty<ForeignKeySnapshot>(),
            new[] { Ix("CategoryName") }),

        new TableSnapshot("Region",
            new[] { R("RegionID"), R("RegionDescription") },
            new[] { "RegionID" },
            Array.Empty<ForeignKeySnapshot>(),
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("Territories",
            new[] { R("TerritoryID"), R("TerritoryDescription"), R("RegionID") },
            new[] { "TerritoryID" },
            new[] { Fk("RegionID", "Region", "RegionID") },
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("Suppliers",
            new[] { R("SupplierID"), R("CompanyName"), N("ContactName"), N("ContactTitle"),
                    N("Address"), N("City"), N("Region"), N("PostalCode"), N("Country"),
                    N("Phone"), N("Fax"), N("HomePage") },
            new[] { "SupplierID" },
            Array.Empty<ForeignKeySnapshot>(),
            new[] { Ix("CompanyName"), Ix("PostalCode") }),

        new TableSnapshot("Customers",
            new[] { R("CustomerID"), R("CompanyName"), N("ContactName"), N("ContactTitle"),
                    N("Address"), N("City"), N("Region"), N("PostalCode"), N("Country"),
                    N("Phone"), N("Fax") },
            new[] { "CustomerID" },
            Array.Empty<ForeignKeySnapshot>(),
            new[] { Ix("City"), Ix("CompanyName"), Ix("PostalCode"), Ix("Region") }),

        new TableSnapshot("CustomerDemographics",
            new[] { R("CustomerTypeID"), N("CustomerDesc") },
            new[] { "CustomerTypeID" },
            Array.Empty<ForeignKeySnapshot>(),
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("CustomerCustomerDemo",
            new[] { R("CustomerID"), R("CustomerTypeID") },
            new[] { "CustomerID", "CustomerTypeID" },
            new[] { Fk("CustomerID", "Customers", "CustomerID"),
                    Fk("CustomerTypeID", "CustomerDemographics", "CustomerTypeID") },
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("Employees",
            new[] { R("EmployeeID"), R("LastName"), R("FirstName"), N("Title"), N("TitleOfCourtesy"),
                    N("BirthDate"), N("HireDate"), N("Address"), N("City"), N("Region"),
                    N("PostalCode"), N("Country"), N("HomePhone"), N("Extension"), N("Photo"),
                    N("Notes"), N("ReportsTo"), N("PhotoPath") },
            new[] { "EmployeeID" },
            new[] { Fk("ReportsTo", "Employees", "EmployeeID") },
            new[] { Ix("LastName"), Ix("PostalCode") }),

        new TableSnapshot("EmployeeTerritories",
            new[] { R("EmployeeID"), R("TerritoryID") },
            new[] { "EmployeeID", "TerritoryID" },
            new[] { Fk("EmployeeID", "Employees", "EmployeeID"),
                    Fk("TerritoryID", "Territories", "TerritoryID") },
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("Shippers",
            new[] { R("ShipperID"), R("CompanyName"), N("Phone") },
            new[] { "ShipperID" },
            Array.Empty<ForeignKeySnapshot>(),
            Array.Empty<IndexSnapshot>()),

        new TableSnapshot("Products",
            new[] { R("ProductID"), R("ProductName"), N("SupplierID"), N("CategoryID"),
                    N("QuantityPerUnit"), N("UnitPrice"), N("UnitsInStock"), N("UnitsOnOrder"),
                    N("ReorderLevel"), R("Discontinued") },
            new[] { "ProductID" },
            new[] { Fk("SupplierID", "Suppliers", "SupplierID"),
                    Fk("CategoryID", "Categories", "CategoryID") },
            new[] { Ix("CategoryID"), Ix("ProductName"), Ix("SupplierID") }),

        new TableSnapshot("Orders",
            new[] { R("OrderID"), N("CustomerID"), N("EmployeeID"), N("OrderDate"), N("RequiredDate"),
                    N("ShippedDate"), N("ShipVia"), N("Freight"), N("ShipName"), N("ShipAddress"),
                    N("ShipCity"), N("ShipRegion"), N("ShipPostalCode"), N("ShipCountry") },
            new[] { "OrderID" },
            new[] { Fk("CustomerID", "Customers", "CustomerID"),
                    Fk("EmployeeID", "Employees", "EmployeeID"),
                    Fk("ShipVia", "Shippers", "ShipperID") },
            new[] { Ix("CustomerID"), Ix("EmployeeID"), Ix("OrderDate"),
                    Ix("ShippedDate"), Ix("ShipVia"), Ix("ShipPostalCode") }),

        new TableSnapshot("Order Details",
            new[] { R("OrderID"), R("ProductID"), R("UnitPrice"), R("Quantity"), R("Discount") },
            new[] { "OrderID", "ProductID" },
            new[] { Fk("OrderID", "Orders", "OrderID"),
                    Fk("ProductID", "Products", "ProductID") },
            new[] { Ix("OrderID"), Ix("ProductID") }),
    });
}
