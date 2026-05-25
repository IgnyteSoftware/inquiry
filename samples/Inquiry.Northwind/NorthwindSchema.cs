namespace Inquiry.Northwind;

/// <summary>
/// Single source of truth for the classic Northwind schema DDL.
/// </summary>
/// <remarks>
/// All 13 classic tables are emitted so the schema is a faithful Northwind. Three of them —
/// <c>Order Details</c>, <c>EmployeeTerritories</c>, and <c>CustomerCustomerDemo</c> — have
/// composite primary keys, which Inquiry does not currently support. They exist in the schema
/// but have no entity or store; consumers that need to read or write them must do so via
/// <c>IInquiry.ExecuteAsync</c> / <c>QueryAsync</c> with raw SQL.
/// </remarks>
public static class NorthwindSchema
{
    /// <summary>
    /// SQLite DDL for the full classic Northwind schema. Idempotent: every CREATE uses
    /// <c>IF NOT EXISTS</c> so re-running against the same database is safe.
    /// </summary>
    /// <remarks>
    /// Declared <c>static readonly</c> rather than <c>const</c> so that consuming assemblies
    /// must perform a runtime field load to read it. That forces the runtime to load
    /// <c>Inquiry.Northwind.dll</c> into the AppDomain, which is what makes
    /// <c>AddInquiry()</c>'s assembly-scanning DI discovery see the generated store
    /// registrations in this package.
    /// </remarks>
    public static readonly string SqliteDdl = """
        CREATE TABLE IF NOT EXISTS Categories (
            CategoryID    INTEGER PRIMARY KEY AUTOINCREMENT,
            CategoryName  TEXT NOT NULL,
            Description   TEXT,
            Picture       BLOB
        );

        CREATE TABLE IF NOT EXISTS Region (
            RegionID           INTEGER PRIMARY KEY NOT NULL,
            RegionDescription  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Territories (
            TerritoryID           TEXT PRIMARY KEY NOT NULL,
            TerritoryDescription  TEXT NOT NULL,
            RegionID              INTEGER NOT NULL,
            FOREIGN KEY (RegionID) REFERENCES Region(RegionID)
        );

        CREATE TABLE IF NOT EXISTS Suppliers (
            SupplierID    INTEGER PRIMARY KEY AUTOINCREMENT,
            CompanyName   TEXT NOT NULL,
            ContactName   TEXT,
            ContactTitle  TEXT,
            Address       TEXT,
            City          TEXT,
            Region        TEXT,
            PostalCode    TEXT,
            Country       TEXT,
            Phone         TEXT,
            Fax           TEXT,
            HomePage      TEXT
        );

        CREATE TABLE IF NOT EXISTS Customers (
            CustomerID    TEXT PRIMARY KEY NOT NULL,
            CompanyName   TEXT NOT NULL,
            ContactName   TEXT,
            ContactTitle  TEXT,
            Address       TEXT,
            City          TEXT,
            Region        TEXT,
            PostalCode    TEXT,
            Country       TEXT,
            Phone         TEXT,
            Fax           TEXT
        );

        CREATE TABLE IF NOT EXISTS CustomerDemographics (
            CustomerTypeID  TEXT PRIMARY KEY NOT NULL,
            CustomerDesc    TEXT
        );

        CREATE TABLE IF NOT EXISTS CustomerCustomerDemo (
            CustomerID      TEXT NOT NULL,
            CustomerTypeID  TEXT NOT NULL,
            PRIMARY KEY (CustomerID, CustomerTypeID),
            FOREIGN KEY (CustomerID)     REFERENCES Customers(CustomerID),
            FOREIGN KEY (CustomerTypeID) REFERENCES CustomerDemographics(CustomerTypeID)
        );

        CREATE TABLE IF NOT EXISTS Employees (
            EmployeeID       INTEGER PRIMARY KEY AUTOINCREMENT,
            LastName         TEXT NOT NULL,
            FirstName        TEXT NOT NULL,
            Title            TEXT,
            TitleOfCourtesy  TEXT,
            BirthDate        TEXT,
            HireDate         TEXT,
            Address          TEXT,
            City             TEXT,
            Region           TEXT,
            PostalCode       TEXT,
            Country          TEXT,
            HomePhone        TEXT,
            Extension        TEXT,
            Photo            BLOB,
            Notes            TEXT,
            ReportsTo        INTEGER,
            PhotoPath        TEXT,
            FOREIGN KEY (ReportsTo) REFERENCES Employees(EmployeeID)
        );

        CREATE TABLE IF NOT EXISTS EmployeeTerritories (
            EmployeeID   INTEGER NOT NULL,
            TerritoryID  TEXT NOT NULL,
            PRIMARY KEY (EmployeeID, TerritoryID),
            FOREIGN KEY (EmployeeID)  REFERENCES Employees(EmployeeID),
            FOREIGN KEY (TerritoryID) REFERENCES Territories(TerritoryID)
        );

        CREATE TABLE IF NOT EXISTS Shippers (
            ShipperID    INTEGER PRIMARY KEY AUTOINCREMENT,
            CompanyName  TEXT NOT NULL,
            Phone        TEXT
        );

        CREATE TABLE IF NOT EXISTS Products (
            ProductID        INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductName      TEXT NOT NULL,
            SupplierID       INTEGER,
            CategoryID       INTEGER,
            QuantityPerUnit  TEXT,
            UnitPrice        NUMERIC DEFAULT 0,
            UnitsInStock     INTEGER DEFAULT 0,
            UnitsOnOrder     INTEGER DEFAULT 0,
            ReorderLevel     INTEGER DEFAULT 0,
            Discontinued     INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY (SupplierID) REFERENCES Suppliers(SupplierID),
            FOREIGN KEY (CategoryID) REFERENCES Categories(CategoryID)
        );

        CREATE TABLE IF NOT EXISTS Orders (
            OrderID         INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerID      TEXT,
            EmployeeID      INTEGER,
            OrderDate       TEXT,
            RequiredDate    TEXT,
            ShippedDate     TEXT,
            ShipVia         INTEGER,
            Freight         NUMERIC DEFAULT 0,
            ShipName        TEXT,
            ShipAddress     TEXT,
            ShipCity        TEXT,
            ShipRegion      TEXT,
            ShipPostalCode  TEXT,
            ShipCountry     TEXT,
            FOREIGN KEY (CustomerID) REFERENCES Customers(CustomerID),
            FOREIGN KEY (EmployeeID) REFERENCES Employees(EmployeeID),
            FOREIGN KEY (ShipVia)    REFERENCES Shippers(ShipperID)
        );

        CREATE TABLE IF NOT EXISTS "Order Details" (
            OrderID    INTEGER NOT NULL,
            ProductID  INTEGER NOT NULL,
            UnitPrice  NUMERIC NOT NULL DEFAULT 0,
            Quantity   INTEGER NOT NULL DEFAULT 1,
            Discount   REAL    NOT NULL DEFAULT 0,
            PRIMARY KEY (OrderID, ProductID),
            FOREIGN KEY (OrderID)   REFERENCES Orders(OrderID),
            FOREIGN KEY (ProductID) REFERENCES Products(ProductID)
        );
        """;
}
