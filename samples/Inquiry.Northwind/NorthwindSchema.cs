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

        CREATE INDEX IF NOT EXISTS IX_Categories_CategoryName ON Categories (CategoryName);
        CREATE INDEX IF NOT EXISTS IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        CREATE INDEX IF NOT EXISTS IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Customers_City ON Customers (City);
        CREATE INDEX IF NOT EXISTS IX_Customers_CompanyName ON Customers (CompanyName);
        CREATE INDEX IF NOT EXISTS IX_Customers_PostalCode ON Customers (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Customers_Region ON Customers (Region);
        CREATE INDEX IF NOT EXISTS IX_Employees_LastName ON Employees (LastName);
        CREATE INDEX IF NOT EXISTS IX_Employees_PostalCode ON Employees (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Products_CategoryID ON Products (CategoryID);
        CREATE INDEX IF NOT EXISTS IX_Products_ProductName ON Products (ProductName);
        CREATE INDEX IF NOT EXISTS IX_Products_SupplierID ON Products (SupplierID);
        CREATE INDEX IF NOT EXISTS IX_Orders_CustomerID ON Orders (CustomerID);
        CREATE INDEX IF NOT EXISTS IX_Orders_EmployeeID ON Orders (EmployeeID);
        CREATE INDEX IF NOT EXISTS IX_Orders_OrderDate ON Orders (OrderDate);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShippedDate ON Orders (ShippedDate);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShipVia ON Orders (ShipVia);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_OrderID" ON "Order Details" (OrderID);
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_ProductID" ON "Order Details" (ProductID);
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
                PostalCode    NVARCHAR(20) NULL,
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
                PostalCode       NVARCHAR(20) NULL,
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
                ShipPostalCode  NVARCHAR(20) NULL,
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

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Categories_CategoryName') CREATE INDEX IX_Categories_CategoryName ON Categories (CategoryName);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Suppliers_CompanyName') CREATE INDEX IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Suppliers_PostalCode') CREATE INDEX IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_City') CREATE INDEX IX_Customers_City ON Customers (City);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_CompanyName') CREATE INDEX IX_Customers_CompanyName ON Customers (CompanyName);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_PostalCode') CREATE INDEX IX_Customers_PostalCode ON Customers (PostalCode);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_Region') CREATE INDEX IX_Customers_Region ON Customers (Region);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_LastName') CREATE INDEX IX_Employees_LastName ON Employees (LastName);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Employees_PostalCode') CREATE INDEX IX_Employees_PostalCode ON Employees (PostalCode);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_CategoryID') CREATE INDEX IX_Products_CategoryID ON Products (CategoryID);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_ProductName') CREATE INDEX IX_Products_ProductName ON Products (ProductName);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_SupplierID') CREATE INDEX IX_Products_SupplierID ON Products (SupplierID);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_CustomerID') CREATE INDEX IX_Orders_CustomerID ON Orders (CustomerID);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_EmployeeID') CREATE INDEX IX_Orders_EmployeeID ON Orders (EmployeeID);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_OrderDate') CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_ShippedDate') CREATE INDEX IX_Orders_ShippedDate ON Orders (ShippedDate);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_ShipVia') CREATE INDEX IX_Orders_ShipVia ON Orders (ShipVia);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Orders_ShipPostalCode') CREATE INDEX IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_Details_OrderID') CREATE INDEX IX_Order_Details_OrderID ON [Order Details] (OrderID);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_Details_ProductID') CREATE INDEX IX_Order_Details_ProductID ON [Order Details] (ProductID);
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

        CREATE INDEX IF NOT EXISTS "IX_Categories_CategoryName" ON "Categories" ("CategoryName");
        CREATE INDEX IF NOT EXISTS "IX_Suppliers_CompanyName" ON "Suppliers" ("CompanyName");
        CREATE INDEX IF NOT EXISTS "IX_Suppliers_PostalCode" ON "Suppliers" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Customers_City" ON "Customers" ("City");
        CREATE INDEX IF NOT EXISTS "IX_Customers_CompanyName" ON "Customers" ("CompanyName");
        CREATE INDEX IF NOT EXISTS "IX_Customers_PostalCode" ON "Customers" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Customers_Region" ON "Customers" ("Region");
        CREATE INDEX IF NOT EXISTS "IX_Employees_LastName" ON "Employees" ("LastName");
        CREATE INDEX IF NOT EXISTS "IX_Employees_PostalCode" ON "Employees" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Products_CategoryID" ON "Products" ("CategoryID");
        CREATE INDEX IF NOT EXISTS "IX_Products_ProductName" ON "Products" ("ProductName");
        CREATE INDEX IF NOT EXISTS "IX_Products_SupplierID" ON "Products" ("SupplierID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_CustomerID" ON "Orders" ("CustomerID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_EmployeeID" ON "Orders" ("EmployeeID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_OrderDate" ON "Orders" ("OrderDate");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShippedDate" ON "Orders" ("ShippedDate");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShipVia" ON "Orders" ("ShipVia");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShipPostalCode" ON "Orders" ("ShipPostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_OrderID" ON "Order Details" ("OrderID");
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_ProductID" ON "Order Details" ("ProductID");
        """;

    /// <summary>
    /// MySQL/MariaDB DDL for the full classic Northwind schema. Idempotent: every CREATE uses
    /// <c>IF NOT EXISTS</c> so re-running against the same database is safe.
    /// </summary>
    /// <remarks>
    /// Identifiers are backtick-quoted (MySQL's native quoting). Generated keys use
    /// <c>AUTO_INCREMENT</c>; string primary/foreign keys use bounded <c>VARCHAR(n)</c> because MySQL
    /// cannot index <c>LONGTEXT</c>; unbounded text and blobs use <c>LONGTEXT</c>/<c>LONGBLOB</c> for
    /// parity with SQLite's unbounded TEXT/BLOB; <c>bool</c> maps to <c>TINYINT(1)</c>.
    /// </remarks>
    public static readonly string MySqlDdl = """
        CREATE TABLE IF NOT EXISTS `Categories` (
            `CategoryID`    INT AUTO_INCREMENT PRIMARY KEY,
            `CategoryName`  VARCHAR(40) NOT NULL,
            `Description`   LONGTEXT,
            `Picture`       LONGBLOB
        );

        CREATE TABLE IF NOT EXISTS `Region` (
            `RegionID`           INT PRIMARY KEY NOT NULL,
            `RegionDescription`  LONGTEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS `Territories` (
            `TerritoryID`           VARCHAR(40) PRIMARY KEY NOT NULL,
            `TerritoryDescription`  LONGTEXT NOT NULL,
            `RegionID`              INT NOT NULL,
            FOREIGN KEY (`RegionID`) REFERENCES `Region`(`RegionID`)
        );

        CREATE TABLE IF NOT EXISTS `Suppliers` (
            `SupplierID`    INT AUTO_INCREMENT PRIMARY KEY,
            `CompanyName`   VARCHAR(40) NOT NULL,
            `ContactName`   LONGTEXT,
            `ContactTitle`  LONGTEXT,
            `Address`       LONGTEXT,
            `City`          LONGTEXT,
            `Region`        LONGTEXT,
            `PostalCode`    LONGTEXT,
            `Country`       LONGTEXT,
            `Phone`         LONGTEXT,
            `Fax`           LONGTEXT,
            `HomePage`      LONGTEXT
        );

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
            PRIMARY KEY (`CustomerID`, `CustomerTypeID`),
            FOREIGN KEY (`CustomerID`)     REFERENCES `Customers`(`CustomerID`),
            FOREIGN KEY (`CustomerTypeID`) REFERENCES `CustomerDemographics`(`CustomerTypeID`)
        );

        CREATE TABLE IF NOT EXISTS `Employees` (
            `EmployeeID`       INT AUTO_INCREMENT PRIMARY KEY,
            `LastName`         VARCHAR(40) NOT NULL,
            `FirstName`        VARCHAR(40) NOT NULL,
            `Title`            LONGTEXT,
            `TitleOfCourtesy`  LONGTEXT,
            `BirthDate`        DATETIME,
            `HireDate`         DATETIME,
            `Address`          LONGTEXT,
            `City`             LONGTEXT,
            `Region`           LONGTEXT,
            `PostalCode`       LONGTEXT,
            `Country`          LONGTEXT,
            `HomePhone`        LONGTEXT,
            `Extension`        LONGTEXT,
            `Photo`            LONGBLOB,
            `Notes`            LONGTEXT,
            `ReportsTo`        INT,
            `PhotoPath`        LONGTEXT,
            FOREIGN KEY (`ReportsTo`) REFERENCES `Employees`(`EmployeeID`)
        );

        CREATE TABLE IF NOT EXISTS `EmployeeTerritories` (
            `EmployeeID`   INT NOT NULL,
            `TerritoryID`  VARCHAR(40) NOT NULL,
            PRIMARY KEY (`EmployeeID`, `TerritoryID`),
            FOREIGN KEY (`EmployeeID`)  REFERENCES `Employees`(`EmployeeID`),
            FOREIGN KEY (`TerritoryID`) REFERENCES `Territories`(`TerritoryID`)
        );

        CREATE TABLE IF NOT EXISTS `Shippers` (
            `ShipperID`    INT AUTO_INCREMENT PRIMARY KEY,
            `CompanyName`  VARCHAR(40) NOT NULL,
            `Phone`        LONGTEXT
        );

        CREATE TABLE IF NOT EXISTS `Products` (
            `ProductID`        INT AUTO_INCREMENT PRIMARY KEY,
            `ProductName`      VARCHAR(40) NOT NULL,
            `SupplierID`       INT,
            `CategoryID`       INT,
            `QuantityPerUnit`  LONGTEXT,
            `UnitPrice`        DECIMAL(19,4) DEFAULT 0,
            `UnitsInStock`     SMALLINT      DEFAULT 0,
            `UnitsOnOrder`     SMALLINT      DEFAULT 0,
            `ReorderLevel`     SMALLINT      DEFAULT 0,
            `Discontinued`     TINYINT(1)    NOT NULL DEFAULT 0,
            FOREIGN KEY (`SupplierID`) REFERENCES `Suppliers`(`SupplierID`),
            FOREIGN KEY (`CategoryID`) REFERENCES `Categories`(`CategoryID`)
        );

        CREATE TABLE IF NOT EXISTS `Orders` (
            `OrderID`         INT AUTO_INCREMENT PRIMARY KEY,
            `CustomerID`      VARCHAR(5),
            `EmployeeID`      INT,
            `OrderDate`       DATETIME,
            `RequiredDate`    DATETIME,
            `ShippedDate`     DATETIME,
            `ShipVia`         INT,
            `Freight`         DECIMAL(19,4) DEFAULT 0,
            `ShipName`        LONGTEXT,
            `ShipAddress`     LONGTEXT,
            `ShipCity`        LONGTEXT,
            `ShipRegion`      LONGTEXT,
            `ShipPostalCode`  LONGTEXT,
            `ShipCountry`     LONGTEXT,
            FOREIGN KEY (`CustomerID`) REFERENCES `Customers`(`CustomerID`),
            FOREIGN KEY (`EmployeeID`) REFERENCES `Employees`(`EmployeeID`),
            FOREIGN KEY (`ShipVia`)    REFERENCES `Shippers`(`ShipperID`)
        );

        CREATE TABLE IF NOT EXISTS `Order Details` (
            `OrderID`    INT NOT NULL,
            `ProductID`  INT NOT NULL,
            `UnitPrice`  DECIMAL(19,4) NOT NULL DEFAULT 0,
            `Quantity`   SMALLINT      NOT NULL DEFAULT 1,
            `Discount`   FLOAT         NOT NULL DEFAULT 0,
            PRIMARY KEY (`OrderID`, `ProductID`),
            FOREIGN KEY (`OrderID`)   REFERENCES `Orders`(`OrderID`),
            FOREIGN KEY (`ProductID`) REFERENCES `Products`(`ProductID`)
        );

        CREATE INDEX IX_Categories_CategoryName ON `Categories` (`CategoryName`);
        CREATE INDEX IX_Suppliers_CompanyName ON `Suppliers` (`CompanyName`);
        CREATE INDEX IX_Suppliers_PostalCode ON `Suppliers` (`PostalCode`(20));
        CREATE INDEX IX_Customers_City ON `Customers` (`City`(50));
        CREATE INDEX IX_Customers_CompanyName ON `Customers` (`CompanyName`);
        CREATE INDEX IX_Customers_PostalCode ON `Customers` (`PostalCode`(20));
        CREATE INDEX IX_Customers_Region ON `Customers` (`Region`(50));
        CREATE INDEX IX_Employees_LastName ON `Employees` (`LastName`);
        CREATE INDEX IX_Employees_PostalCode ON `Employees` (`PostalCode`(20));
        CREATE INDEX IX_Products_CategoryID ON `Products` (`CategoryID`);
        CREATE INDEX IX_Products_ProductName ON `Products` (`ProductName`);
        CREATE INDEX IX_Products_SupplierID ON `Products` (`SupplierID`);
        CREATE INDEX IX_Orders_CustomerID ON `Orders` (`CustomerID`);
        CREATE INDEX IX_Orders_EmployeeID ON `Orders` (`EmployeeID`);
        CREATE INDEX IX_Orders_OrderDate ON `Orders` (`OrderDate`);
        CREATE INDEX IX_Orders_ShippedDate ON `Orders` (`ShippedDate`);
        CREATE INDEX IX_Orders_ShipVia ON `Orders` (`ShipVia`);
        CREATE INDEX IX_Orders_ShipPostalCode ON `Orders` (`ShipPostalCode`(20));
        CREATE INDEX IX_Order_Details_OrderID ON `Order Details` (`OrderID`);
        CREATE INDEX IX_Order_Details_ProductID ON `Order Details` (`ProductID`);
        """;

    /// <summary>
    /// Oracle 12c+ DDL for the full classic Northwind schema. NOT idempotent — Oracle has no
    /// <c>CREATE TABLE IF NOT EXISTS</c>; the test harness creates a throwaway schema per run, so
    /// re-creation safety is unnecessary.
    /// </summary>
    /// <remarks>
    /// Identifiers are left unquoted to match the Oracle SQL builder's unquoted policy: Oracle folds
    /// unquoted identifiers to uppercase, so quoted mixed-case DDL would not resolve against the
    /// builder's unquoted SQL. The one exception is <c>"Order Details"</c>, whose embedded space forces
    /// quoting in every dialect. Generated keys use <c>GENERATED BY DEFAULT AS IDENTITY</c> (12c+);
    /// <c>NUMBER(p)</c> uses precise precision so <c>reader.GetInt32</c>/<c>GetInt16</c> don't trip
    /// NUMBER→decimal coercion; <c>Guid</c> would map to <c>RAW(16)</c>; <c>bool</c> maps to
    /// <c>NUMBER(1)</c>; unbounded text/blobs use <c>CLOB</c>/<c>BLOB</c>; timestamps use
    /// <c>TIMESTAMP</c>.
    /// </remarks>
    public static readonly string OracleDdl = """
        CREATE TABLE Categories (
            CategoryID    NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            CategoryName  VARCHAR2(40) NOT NULL,
            Description   CLOB,
            Picture       BLOB
        );

        CREATE TABLE Region (
            RegionID           NUMBER(10) PRIMARY KEY NOT NULL,
            RegionDescription  VARCHAR2(60) NOT NULL
        );

        CREATE TABLE Territories (
            TerritoryID           VARCHAR2(40) PRIMARY KEY NOT NULL,
            TerritoryDescription  VARCHAR2(60) NOT NULL,
            RegionID              NUMBER(10) NOT NULL,
            FOREIGN KEY (RegionID) REFERENCES Region(RegionID)
        );

        CREATE TABLE Suppliers (
            SupplierID    NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            CompanyName   VARCHAR2(40) NOT NULL,
            ContactName   VARCHAR2(60),
            ContactTitle  VARCHAR2(60),
            Address       VARCHAR2(120),
            City          VARCHAR2(60),
            Region        VARCHAR2(60),
            PostalCode    VARCHAR2(20),
            Country       VARCHAR2(60),
            Phone         VARCHAR2(40),
            Fax           VARCHAR2(40),
            HomePage      CLOB
        );

        CREATE TABLE Customers (
            CustomerID    VARCHAR2(5) PRIMARY KEY NOT NULL,
            CompanyName   VARCHAR2(40) NOT NULL,
            ContactName   VARCHAR2(60),
            ContactTitle  VARCHAR2(60),
            Address       VARCHAR2(120),
            City          VARCHAR2(60),
            Region        VARCHAR2(60),
            PostalCode    VARCHAR2(20),
            Country       VARCHAR2(60),
            Phone         VARCHAR2(40),
            Fax           VARCHAR2(40)
        );

        CREATE TABLE CustomerDemographics (
            CustomerTypeID  VARCHAR2(10) PRIMARY KEY NOT NULL,
            CustomerDesc    CLOB
        );

        CREATE TABLE CustomerCustomerDemo (
            CustomerID      VARCHAR2(5)  NOT NULL,
            CustomerTypeID  VARCHAR2(10) NOT NULL,
            PRIMARY KEY (CustomerID, CustomerTypeID),
            FOREIGN KEY (CustomerID)     REFERENCES Customers(CustomerID),
            FOREIGN KEY (CustomerTypeID) REFERENCES CustomerDemographics(CustomerTypeID)
        );

        CREATE TABLE Employees (
            EmployeeID       NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            LastName         VARCHAR2(40) NOT NULL,
            FirstName        VARCHAR2(40) NOT NULL,
            Title            VARCHAR2(60),
            TitleOfCourtesy  VARCHAR2(40),
            BirthDate        TIMESTAMP,
            HireDate         TIMESTAMP,
            Address          VARCHAR2(120),
            City             VARCHAR2(60),
            Region           VARCHAR2(60),
            PostalCode       VARCHAR2(20),
            Country          VARCHAR2(60),
            HomePhone        VARCHAR2(40),
            Extension        VARCHAR2(10),
            Photo            BLOB,
            Notes            CLOB,
            ReportsTo        NUMBER(10),
            PhotoPath        VARCHAR2(255),
            FOREIGN KEY (ReportsTo) REFERENCES Employees(EmployeeID)
        );

        CREATE TABLE EmployeeTerritories (
            EmployeeID   NUMBER(10)   NOT NULL,
            TerritoryID  VARCHAR2(40) NOT NULL,
            PRIMARY KEY (EmployeeID, TerritoryID),
            FOREIGN KEY (EmployeeID)  REFERENCES Employees(EmployeeID),
            FOREIGN KEY (TerritoryID) REFERENCES Territories(TerritoryID)
        );

        CREATE TABLE Shippers (
            ShipperID    NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            CompanyName  VARCHAR2(40) NOT NULL,
            Phone        VARCHAR2(40)
        );

        CREATE TABLE Products (
            ProductID        NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            ProductName      VARCHAR2(40) NOT NULL,
            SupplierID       NUMBER(10),
            CategoryID       NUMBER(10),
            QuantityPerUnit  VARCHAR2(40),
            UnitPrice        NUMBER(19,4) DEFAULT 0,
            UnitsInStock     NUMBER(5)    DEFAULT 0,
            UnitsOnOrder     NUMBER(5)    DEFAULT 0,
            ReorderLevel     NUMBER(5)    DEFAULT 0,
            Discontinued     NUMBER(1)    DEFAULT 0 NOT NULL,
            FOREIGN KEY (SupplierID) REFERENCES Suppliers(SupplierID),
            FOREIGN KEY (CategoryID) REFERENCES Categories(CategoryID)
        );

        CREATE TABLE Orders (
            OrderID         NUMBER(10) GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
            CustomerID      VARCHAR2(5),
            EmployeeID      NUMBER(10),
            OrderDate       TIMESTAMP,
            RequiredDate    TIMESTAMP,
            ShippedDate     TIMESTAMP,
            ShipVia         NUMBER(10),
            Freight         NUMBER(19,4) DEFAULT 0,
            ShipName        VARCHAR2(40),
            ShipAddress     VARCHAR2(120),
            ShipCity        VARCHAR2(60),
            ShipRegion      VARCHAR2(60),
            ShipPostalCode  VARCHAR2(20),
            ShipCountry     VARCHAR2(60),
            FOREIGN KEY (CustomerID) REFERENCES Customers(CustomerID),
            FOREIGN KEY (EmployeeID) REFERENCES Employees(EmployeeID),
            FOREIGN KEY (ShipVia)    REFERENCES Shippers(ShipperID)
        );

        CREATE TABLE "Order Details" (
            OrderID    NUMBER(10) NOT NULL,
            ProductID  NUMBER(10) NOT NULL,
            UnitPrice  NUMBER(19,4) DEFAULT 0 NOT NULL,
            Quantity   NUMBER(5)    DEFAULT 1 NOT NULL,
            Discount   BINARY_FLOAT DEFAULT 0 NOT NULL,
            PRIMARY KEY (OrderID, ProductID),
            FOREIGN KEY (OrderID)   REFERENCES Orders(OrderID),
            FOREIGN KEY (ProductID) REFERENCES Products(ProductID)
        );

        CREATE INDEX IX_Categories_CategoryName ON Categories (CategoryName);
        CREATE INDEX IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        CREATE INDEX IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        CREATE INDEX IX_Customers_City ON Customers (City);
        CREATE INDEX IX_Customers_CompanyName ON Customers (CompanyName);
        CREATE INDEX IX_Customers_PostalCode ON Customers (PostalCode);
        CREATE INDEX IX_Customers_Region ON Customers (Region);
        CREATE INDEX IX_Employees_LastName ON Employees (LastName);
        CREATE INDEX IX_Employees_PostalCode ON Employees (PostalCode);
        CREATE INDEX IX_Products_CategoryID ON Products (CategoryID);
        CREATE INDEX IX_Products_ProductName ON Products (ProductName);
        CREATE INDEX IX_Products_SupplierID ON Products (SupplierID);
        CREATE INDEX IX_Orders_CustomerID ON Orders (CustomerID);
        CREATE INDEX IX_Orders_EmployeeID ON Orders (EmployeeID);
        CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate);
        CREATE INDEX IX_Orders_ShippedDate ON Orders (ShippedDate);
        CREATE INDEX IX_Orders_ShipVia ON Orders (ShipVia);
        CREATE INDEX IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
        CREATE INDEX IX_OrderDetails_OrderID ON "Order Details" (OrderID);
        CREATE INDEX IX_OrderDetails_ProductID ON "Order Details" (ProductID);
        """;
}
