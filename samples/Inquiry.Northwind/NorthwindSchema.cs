namespace Inquiry.Northwind;

/// <summary>
/// Single source of truth for the classic Northwind schema DDL, expressed once per
/// supported Inquiry provider.
/// </summary>
/// <remarks>
/// All 13 classic tables are emitted so the schema is a faithful Northwind. Each table has
/// a matching entity and generated store under <c>Inquiry.Northwind.Models</c> /
/// <c>Inquiry.Northwind.Stores</c>, including the three composite-key tables
/// (<c>Order Details</c>, <c>EmployeeTerritories</c>, <c>CustomerCustomerDemo</c>) — Inquiry
/// supports composite primary keys so they are first-class generated stores like every
/// other table.
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

    /// <summary>
    /// SQL Server DDL for the full classic Northwind schema. Idempotent: every CREATE is
    /// wrapped in an <c>OBJECT_ID()</c> existence check so re-running against the same
    /// database is safe.
    /// </summary>
    /// <remarks>
    /// String primary keys use bounded <c>NVARCHAR</c> because SQL Server cannot key on
    /// <c>NVARCHAR(MAX)</c>; all other text columns use <c>NVARCHAR(MAX)</c> for parity with
    /// SQLite's unbounded TEXT. The <c>[Order Details]</c> table is bracket-quoted because
    /// of the space in its name.
    /// </remarks>
    public static readonly string SqlServerDdl = """
        IF OBJECT_ID(N'Categories', N'U') IS NULL
        BEGIN
            CREATE TABLE Categories (
                CategoryID    INT IDENTITY(1,1) PRIMARY KEY,
                CategoryName  NVARCHAR(40) NOT NULL,
                Description   NVARCHAR(MAX) NULL,
                Picture       VARBINARY(MAX) NULL
            );
        END;

        IF OBJECT_ID(N'Region', N'U') IS NULL
        BEGIN
            CREATE TABLE Region (
                RegionID           INT NOT NULL PRIMARY KEY,
                RegionDescription  NVARCHAR(60) NOT NULL
            );
        END;

        IF OBJECT_ID(N'Territories', N'U') IS NULL
        BEGIN
            CREATE TABLE Territories (
                TerritoryID           NVARCHAR(40) NOT NULL PRIMARY KEY,
                TerritoryDescription  NVARCHAR(60) NOT NULL,
                RegionID              INT NOT NULL,
                CONSTRAINT FK_Territories_Region FOREIGN KEY (RegionID) REFERENCES Region(RegionID)
            );
        END;

        IF OBJECT_ID(N'Suppliers', N'U') IS NULL
        BEGIN
            CREATE TABLE Suppliers (
                SupplierID    INT IDENTITY(1,1) PRIMARY KEY,
                CompanyName   NVARCHAR(40) NOT NULL,
                ContactName   NVARCHAR(MAX) NULL,
                ContactTitle  NVARCHAR(MAX) NULL,
                Address       NVARCHAR(MAX) NULL,
                City          NVARCHAR(MAX) NULL,
                Region        NVARCHAR(MAX) NULL,
                PostalCode    NVARCHAR(MAX) NULL,
                Country       NVARCHAR(MAX) NULL,
                Phone         NVARCHAR(MAX) NULL,
                Fax           NVARCHAR(MAX) NULL,
                HomePage      NVARCHAR(MAX) NULL
            );
        END;

        IF OBJECT_ID(N'Customers', N'U') IS NULL
        BEGIN
            CREATE TABLE Customers (
                CustomerID    NVARCHAR(5) NOT NULL PRIMARY KEY,
                CompanyName   NVARCHAR(40) NOT NULL,
                ContactName   NVARCHAR(MAX) NULL,
                ContactTitle  NVARCHAR(MAX) NULL,
                Address       NVARCHAR(MAX) NULL,
                City          NVARCHAR(MAX) NULL,
                Region        NVARCHAR(MAX) NULL,
                PostalCode    NVARCHAR(MAX) NULL,
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
                CONSTRAINT PK_CustomerCustomerDemo PRIMARY KEY (CustomerID, CustomerTypeID),
                CONSTRAINT FK_CCD_Customers     FOREIGN KEY (CustomerID)     REFERENCES Customers(CustomerID),
                CONSTRAINT FK_CCD_Demographics  FOREIGN KEY (CustomerTypeID) REFERENCES CustomerDemographics(CustomerTypeID)
            );
        END;

        IF OBJECT_ID(N'Employees', N'U') IS NULL
        BEGIN
            CREATE TABLE Employees (
                EmployeeID       INT IDENTITY(1,1) PRIMARY KEY,
                LastName         NVARCHAR(40) NOT NULL,
                FirstName        NVARCHAR(40) NOT NULL,
                Title            NVARCHAR(MAX) NULL,
                TitleOfCourtesy  NVARCHAR(MAX) NULL,
                BirthDate        DATETIME NULL,
                HireDate         DATETIME NULL,
                Address          NVARCHAR(MAX) NULL,
                City             NVARCHAR(MAX) NULL,
                Region           NVARCHAR(MAX) NULL,
                PostalCode       NVARCHAR(MAX) NULL,
                Country          NVARCHAR(MAX) NULL,
                HomePhone        NVARCHAR(MAX) NULL,
                Extension        NVARCHAR(MAX) NULL,
                Photo            VARBINARY(MAX) NULL,
                Notes            NVARCHAR(MAX) NULL,
                ReportsTo        INT NULL,
                PhotoPath        NVARCHAR(MAX) NULL,
                CONSTRAINT FK_Employees_ReportsTo FOREIGN KEY (ReportsTo) REFERENCES Employees(EmployeeID)
            );
        END;

        IF OBJECT_ID(N'EmployeeTerritories', N'U') IS NULL
        BEGIN
            CREATE TABLE EmployeeTerritories (
                EmployeeID   INT          NOT NULL,
                TerritoryID  NVARCHAR(40) NOT NULL,
                CONSTRAINT PK_EmployeeTerritories PRIMARY KEY (EmployeeID, TerritoryID),
                CONSTRAINT FK_ET_Employees   FOREIGN KEY (EmployeeID)  REFERENCES Employees(EmployeeID),
                CONSTRAINT FK_ET_Territories FOREIGN KEY (TerritoryID) REFERENCES Territories(TerritoryID)
            );
        END;

        IF OBJECT_ID(N'Shippers', N'U') IS NULL
        BEGIN
            CREATE TABLE Shippers (
                ShipperID    INT IDENTITY(1,1) PRIMARY KEY,
                CompanyName  NVARCHAR(40) NOT NULL,
                Phone        NVARCHAR(MAX) NULL
            );
        END;

        IF OBJECT_ID(N'Products', N'U') IS NULL
        BEGIN
            CREATE TABLE Products (
                ProductID        INT IDENTITY(1,1) PRIMARY KEY,
                ProductName      NVARCHAR(40) NOT NULL,
                SupplierID       INT NULL,
                CategoryID       INT NULL,
                QuantityPerUnit  NVARCHAR(MAX) NULL,
                UnitPrice        DECIMAL(19,4) NULL DEFAULT 0,
                UnitsInStock     SMALLINT NULL DEFAULT 0,
                UnitsOnOrder     SMALLINT NULL DEFAULT 0,
                ReorderLevel     SMALLINT NULL DEFAULT 0,
                Discontinued     BIT NOT NULL DEFAULT 0,
                CONSTRAINT FK_Products_Suppliers   FOREIGN KEY (SupplierID) REFERENCES Suppliers(SupplierID),
                CONSTRAINT FK_Products_Categories  FOREIGN KEY (CategoryID) REFERENCES Categories(CategoryID)
            );
        END;

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
                ShipPostalCode  NVARCHAR(MAX) NULL,
                ShipCountry     NVARCHAR(MAX) NULL,
                CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerID) REFERENCES Customers(CustomerID),
                CONSTRAINT FK_Orders_Employees FOREIGN KEY (EmployeeID) REFERENCES Employees(EmployeeID),
                CONSTRAINT FK_Orders_Shippers  FOREIGN KEY (ShipVia)    REFERENCES Shippers(ShipperID)
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
                CONSTRAINT PK_Order_Details PRIMARY KEY (OrderID, ProductID),
                CONSTRAINT FK_OD_Orders   FOREIGN KEY (OrderID)   REFERENCES Orders(OrderID),
                CONSTRAINT FK_OD_Products FOREIGN KEY (ProductID) REFERENCES Products(ProductID)
            );
        END;
        """;

    /// <summary>
    /// PostgreSQL DDL for the full classic Northwind schema. Idempotent: every CREATE uses
    /// <c>IF NOT EXISTS</c> so re-running against the same database is safe.
    /// </summary>
    /// <remarks>
    /// All identifiers are double-quoted to preserve their original mixed casing — the PostgreSQL
    /// SQL builder emits quoted identifiers, so the tables must be created the same way or the
    /// generated SQL will fail to resolve them. PostgreSQL's <c>SERIAL</c> is used for IDENTITY
    /// columns; <c>BYTEA</c> stands in for SQLite <c>BLOB</c>.
    /// </remarks>
    public static readonly string PostgreSqlDdl = """
        CREATE TABLE IF NOT EXISTS "Categories" (
            "CategoryID"    SERIAL PRIMARY KEY,
            "CategoryName"  TEXT NOT NULL,
            "Description"   TEXT,
            "Picture"       BYTEA
        );

        CREATE TABLE IF NOT EXISTS "Region" (
            "RegionID"           INTEGER PRIMARY KEY NOT NULL,
            "RegionDescription"  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Territories" (
            "TerritoryID"           TEXT PRIMARY KEY NOT NULL,
            "TerritoryDescription"  TEXT NOT NULL,
            "RegionID"              INTEGER NOT NULL,
            FOREIGN KEY ("RegionID") REFERENCES "Region"("RegionID")
        );

        CREATE TABLE IF NOT EXISTS "Suppliers" (
            "SupplierID"    SERIAL PRIMARY KEY,
            "CompanyName"   TEXT NOT NULL,
            "ContactName"   TEXT,
            "ContactTitle"  TEXT,
            "Address"       TEXT,
            "City"          TEXT,
            "Region"        TEXT,
            "PostalCode"    TEXT,
            "Country"       TEXT,
            "Phone"         TEXT,
            "Fax"           TEXT,
            "HomePage"      TEXT
        );

        CREATE TABLE IF NOT EXISTS "Customers" (
            "CustomerID"    TEXT PRIMARY KEY NOT NULL,
            "CompanyName"   TEXT NOT NULL,
            "ContactName"   TEXT,
            "ContactTitle"  TEXT,
            "Address"       TEXT,
            "City"          TEXT,
            "Region"        TEXT,
            "PostalCode"    TEXT,
            "Country"       TEXT,
            "Phone"         TEXT,
            "Fax"           TEXT
        );

        CREATE TABLE IF NOT EXISTS "CustomerDemographics" (
            "CustomerTypeID"  TEXT PRIMARY KEY NOT NULL,
            "CustomerDesc"    TEXT
        );

        CREATE TABLE IF NOT EXISTS "CustomerCustomerDemo" (
            "CustomerID"      TEXT NOT NULL,
            "CustomerTypeID"  TEXT NOT NULL,
            PRIMARY KEY ("CustomerID", "CustomerTypeID"),
            FOREIGN KEY ("CustomerID")     REFERENCES "Customers"("CustomerID"),
            FOREIGN KEY ("CustomerTypeID") REFERENCES "CustomerDemographics"("CustomerTypeID")
        );

        CREATE TABLE IF NOT EXISTS "Employees" (
            "EmployeeID"       SERIAL PRIMARY KEY,
            "LastName"         TEXT NOT NULL,
            "FirstName"        TEXT NOT NULL,
            "Title"            TEXT,
            "TitleOfCourtesy"  TEXT,
            "BirthDate"        TIMESTAMP,
            "HireDate"         TIMESTAMP,
            "Address"          TEXT,
            "City"             TEXT,
            "Region"           TEXT,
            "PostalCode"       TEXT,
            "Country"          TEXT,
            "HomePhone"        TEXT,
            "Extension"        TEXT,
            "Photo"            BYTEA,
            "Notes"            TEXT,
            "ReportsTo"        INTEGER,
            "PhotoPath"        TEXT,
            FOREIGN KEY ("ReportsTo") REFERENCES "Employees"("EmployeeID")
        );

        CREATE TABLE IF NOT EXISTS "EmployeeTerritories" (
            "EmployeeID"   INTEGER NOT NULL,
            "TerritoryID"  TEXT NOT NULL,
            PRIMARY KEY ("EmployeeID", "TerritoryID"),
            FOREIGN KEY ("EmployeeID")  REFERENCES "Employees"("EmployeeID"),
            FOREIGN KEY ("TerritoryID") REFERENCES "Territories"("TerritoryID")
        );

        CREATE TABLE IF NOT EXISTS "Shippers" (
            "ShipperID"    SERIAL PRIMARY KEY,
            "CompanyName"  TEXT NOT NULL,
            "Phone"        TEXT
        );

        CREATE TABLE IF NOT EXISTS "Products" (
            "ProductID"        SERIAL PRIMARY KEY,
            "ProductName"      TEXT NOT NULL,
            "SupplierID"       INTEGER,
            "CategoryID"       INTEGER,
            "QuantityPerUnit"  TEXT,
            "UnitPrice"        NUMERIC(19,4) DEFAULT 0,
            "UnitsInStock"     SMALLINT      DEFAULT 0,
            "UnitsOnOrder"     SMALLINT      DEFAULT 0,
            "ReorderLevel"     SMALLINT      DEFAULT 0,
            "Discontinued"     BOOLEAN       NOT NULL DEFAULT FALSE,
            FOREIGN KEY ("SupplierID") REFERENCES "Suppliers"("SupplierID"),
            FOREIGN KEY ("CategoryID") REFERENCES "Categories"("CategoryID")
        );

        CREATE TABLE IF NOT EXISTS "Orders" (
            "OrderID"         SERIAL PRIMARY KEY,
            "CustomerID"      TEXT,
            "EmployeeID"      INTEGER,
            "OrderDate"       TIMESTAMP,
            "RequiredDate"    TIMESTAMP,
            "ShippedDate"     TIMESTAMP,
            "ShipVia"         INTEGER,
            "Freight"         NUMERIC(19,4) DEFAULT 0,
            "ShipName"        TEXT,
            "ShipAddress"     TEXT,
            "ShipCity"        TEXT,
            "ShipRegion"      TEXT,
            "ShipPostalCode"  TEXT,
            "ShipCountry"     TEXT,
            FOREIGN KEY ("CustomerID") REFERENCES "Customers"("CustomerID"),
            FOREIGN KEY ("EmployeeID") REFERENCES "Employees"("EmployeeID"),
            FOREIGN KEY ("ShipVia")    REFERENCES "Shippers"("ShipperID")
        );

        CREATE TABLE IF NOT EXISTS "Order Details" (
            "OrderID"    INTEGER NOT NULL,
            "ProductID"  INTEGER NOT NULL,
            "UnitPrice"  NUMERIC(19,4) NOT NULL DEFAULT 0,
            "Quantity"   SMALLINT      NOT NULL DEFAULT 1,
            "Discount"   REAL          NOT NULL DEFAULT 0,
            PRIMARY KEY ("OrderID", "ProductID"),
            FOREIGN KEY ("OrderID")   REFERENCES "Orders"("OrderID"),
            FOREIGN KEY ("ProductID") REFERENCES "Products"("ProductID")
        );
        """;
}
