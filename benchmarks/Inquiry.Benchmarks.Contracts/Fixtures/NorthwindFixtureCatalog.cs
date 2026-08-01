namespace Inquiry.Benchmarks.Contracts.Fixtures;

public static class NorthwindFixtureCatalog
{
    public const string ContractVersion = "northwind-v1";
    public const int Seed = 872026;

    public static FixtureSchema Schema { get; } = BuildSchema();
    public static string SchemaHash { get; } = CanonicalHash.Sha256(Schema.CanonicalText);

    // Generated from the row streams. Unit tests recompute the tiny tier; the fixture writer verifies
    // every emitted tier before it can become benchmark evidence.
    private static readonly IReadOnlyDictionary<FixtureTier, IReadOnlyDictionary<string, string>> CheckedChecksums =
        new Dictionary<FixtureTier, IReadOnlyDictionary<string, string>>
        {
            [FixtureTier.Tiny] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Categories"] = "8ba51b64cbcfd48ce5462af874c41a0151e02819c5ee44e716c24f943f2a4280",
                ["Customers"] = "207221294fca387431da63aa4a395e1262a7cdb89afb09ca86b9add3af247b58",
                ["CustomerCustomerDemo"] = "a4f28dc70baeab4dea8c455852780339bc0eb455476560118b1414f479fdc2a2",
                ["CustomerDemographics"] = "512c5adcb479c1ebac727db868a565ea669167028465e06a500b2f39ad0d87f4",
                ["Employees"] = "95e9a1a534e1b46fe97770a6949dc30b6a18658d00c5719dff507566dc5616a1",
                ["EmployeeTerritories"] = "c88685c66a7490254f369682d79b46c5c67ebe73c9b290f95c680f6afd0344e1",
                ["Orders"] = "c3132930a713fe0f43fd08439057a77e1f4c2a47d1cc0a7cbaa2874ac376ff4b",
                ["Order Details"] = "ea2b29ca5ee7d8dfe05614c27d9b07430f8001e425cd142fc952c909d0e056a6",
                ["Products"] = "f6068bc02b4dce4ecc91b9589817ffbd584809b821e1864e24c92d7b638c519a",
                ["Region"] = "75582aa52913a6849949618e8c42eb599e0103b941f545f099fecc86235bbcbe",
                ["Shippers"] = "152d30e7a7e697a2ee35f1790cfea4e4a47f1c714ccf86cf033c67d59855b6ea",
                ["Suppliers"] = "62687e5f9bce68da51d79ae36dbcd0d75ef003ff683906eb5d61f8e761448e02",
                ["Territories"] = "c52bb37d322c8a5f86e0e27456f7bf6f1956ea920d9b28fa1a465ad87e44cd9c",
            },
            [FixtureTier.Standard] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Categories"] = "e60a8abcdb34d60b5fdfef2f5dea31c31ecd3e2fc462898f64bfa7fb062f537b",
                ["Customers"] = "00066d2fd9b54d7330c18ad2bff0d0f92ffe25c55e8f24295fd734bf4a9dabb8",
                ["CustomerCustomerDemo"] = "f8e5cbc46e9d13f17a38d76fa6dd072982bfae77164a6fdfe63d2bb193eeaeba",
                ["CustomerDemographics"] = "ae8ab436eaeb994c925ed06488324423886f9932d55e0058d7ead5434b3d9d1c",
                ["Employees"] = "9de6957e09d94b700073183629e66d89c1e559f061524edcad63781e4b0f2931",
                ["EmployeeTerritories"] = "d9b684e9d6494f1d0c9eae16f58ab40faf2e276b1515a0547e3954c972aa8f5a",
                ["Orders"] = "5f90a423a4aaaa495bdda560556a3e9bc9b7d23ac95c12eca1c24ca4d7bae357",
                ["Order Details"] = "434d93e143634c2994b6c2e2fb67176dc64d69bb75ea4dcf48fa82dac891c94a",
                ["Products"] = "a8160a4afe9b0bce9c98c343191cc7d0f96ed863d00cd017f368b4e6519ba914",
                ["Region"] = "e0c2248c37a5995f97027f85f11974347288dcdb53a0e04627f5803d6909b5e0",
                ["Shippers"] = "93add79e108ae74595ccb946f1c16f7e65e3edda0b4d290fbf8be9749c98f91a",
                ["Suppliers"] = "94cf4108189d475ec2cb5414cbe4e539dd1c9d1c6b070298c245d5b119d620bd",
                ["Territories"] = "7ae03727aced174b36d49667e6bb50b70e836f84ae7b24ca17ccde2e49f3516c",
            },
            [FixtureTier.Large] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Categories"] = "3183a38e64c29581a8c3a0a7930550ecc02fee8afe084fb4ae0ce0eee50341d4",
                ["Customers"] = "25ea44e4801de9fdba51c66bdccb1f817c35c59dd566808c343c5148ce7e57cf",
                ["CustomerCustomerDemo"] = "68887c3c58b94354bd34fe97a3f39235da37ef3f251e42fc83b7023e50922d16",
                ["CustomerDemographics"] = "47cc6da9fc802b882e1a21b7a9b0ba7154a2d8713514f415a3a733175023f541",
                ["Employees"] = "ec9191cbee0e66f23cc3e2e1c40a45446a3f2ca2691a4cdab8394986eb8c17e6",
                ["EmployeeTerritories"] = "733cbaa97711b970388bd06b0226b64ed6277e99a2288784a808bebe41644397",
                ["Orders"] = "6507b714073d9d1a400344795e9c2ba6ec292026ed2054cca71d647706948309",
                ["Order Details"] = "50583316a767ff70b586c2793df595bf101dc603740670c61a93bae5c433e3e8",
                ["Products"] = "ec270c1b1ddab4b91a684b103d5d10238a0e275ff46efe5c76d8c5f0928cb6a7",
                ["Region"] = "898c2a8a7da055aa3c4297b7e54b761da5e2f463a0231cf50f8606a9abb67fcb",
                ["Shippers"] = "a276a479930296d3f42989df1e01bd997b49d0df5a456565ef639f831dfd5bc4",
                ["Suppliers"] = "ff2375a2acca709920c23dff81841e0e3c0b8bc1175d9b7524c1e5571aa9cc0e",
                ["Territories"] = "bf7eda69349cfe1dcf87cd42c96a402e5c75ec670899527b718c59e437d8cbae",
            },
        };

    private static readonly IReadOnlyDictionary<FixtureTier, FixtureManifest> Manifests =
        new Dictionary<FixtureTier, FixtureManifest>
        {
            [FixtureTier.Tiny] = CreateManifest(FixtureTier.Tiny,
                customers: 100, orders: 1_000, details: 5_000, products: 100,
                categories: 8, suppliers: 29, shippers: 6, employees: 9, regions: 4,
                territories: 53, demographics: 5, customerDemos: 100, employeeTerritories: 49),
            [FixtureTier.Standard] = CreateManifest(FixtureTier.Standard,
                customers: 10_000, orders: 100_000, details: 500_000, products: 10_000,
                categories: 32, suppliers: 512, shippers: 16, employees: 64, regions: 16,
                territories: 512, demographics: 20, customerDemos: 10_000, employeeTerritories: 256),
            [FixtureTier.Large] = CreateManifest(FixtureTier.Large,
                customers: 100_000, orders: 1_000_000, details: 5_000_000, products: 100_000,
                categories: 64, suppliers: 4_096, shippers: 32, employees: 256, regions: 32,
                territories: 4_096, demographics: 50, customerDemos: 100_000, employeeTerritories: 2_048),
        };

    public static FixtureManifest For(FixtureTier tier) => Manifests[tier];

    private static FixtureManifest CreateManifest(
        FixtureTier tier,
        int customers,
        int orders,
        int details,
        int products,
        int categories,
        int suppliers,
        int shippers,
        int employees,
        int regions,
        int territories,
        int demographics,
        int customerDemos,
        int employeeTerritories)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Categories"] = categories,
            ["Customers"] = customers,
            ["CustomerCustomerDemo"] = customerDemos,
            ["CustomerDemographics"] = demographics,
            ["Employees"] = employees,
            ["EmployeeTerritories"] = employeeTerritories,
            ["Orders"] = orders,
            ["Order Details"] = details,
            ["Products"] = products,
            ["Region"] = regions,
            ["Shippers"] = shippers,
            ["Suppliers"] = suppliers,
            ["Territories"] = territories,
        };

        var checksums = CheckedChecksums[tier];

        return new FixtureManifest(
            ContractVersion,
            tier,
            Seed,
            SchemaHash,
            counts,
            checksums,
            new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Categories.CategoryID"] = categories + 1L,
                ["Employees.EmployeeID"] = employees + 1L,
                ["Orders.OrderID"] = orders + 1L,
                ["Products.ProductID"] = products + 1L,
                ["Shippers.ShipperID"] = shippers + 1L,
                ["Suppliers.SupplierID"] = suppliers + 1L,
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customer-city"] = "hot:50%;warm:35%;cold:15%",
                ["order-customer"] = "hot:50%@first-1%;warm:35%@next-9%;tail:15%@remaining-90%",
                ["product-category"] = "uniform",
                ["order-date"] = "48-month-uniform",
            },
            "deterministic-xorshift64star;orders-per-customer=10;details-per-order=5",
            "en-US-ordinal-ci",
            "UTC",
            "northwind-portable-v1");
    }

    private static FixtureSchema BuildSchema()
    {
        static FixtureColumn C(string name, string type, bool nullable = false, bool generated = false)
        {
            int? length = type == "String" ? StringLength(name) : null;
            int? precision = type == "Decimal" ? 19 : null;
            int? scale = type == "Decimal" ? 4 : null;
            var databaseType = type switch
            {
                "String" => $"varchar({length})",
                "Int32" => "integer",
                "Int16" => "smallint",
                "Decimal" => $"decimal({precision},{scale})",
                "Single" => "real",
                "DateTime" => "timestamp",
                "Boolean" => "boolean",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown fixture CLR type."),
            };
            return new FixtureColumn(
                name, type, databaseType, nullable, length, precision, scale, generated,
                DefaultExpression: null,
                Collation: type == "String" ? "en-US-ordinal-ci" : null);
        }

        static int StringLength(string name) => name switch
        {
            "CustomerID" => 5,
            "CustomerTypeID" => 10,
            "TerritoryID" => 7,
            "CategoryName" or "City" or "Region" or "Country" or "ShipCity" or "ShipRegion" or "ShipCountry" => 15,
            "FirstName" or "PostalCode" or "ShipPostalCode" => 10,
            "LastName" or "QuantityPerUnit" => 20,
            "Phone" or "Fax" or "HomePhone" => 24,
            "TitleOfCourtesy" => 25,
            "ContactName" or "ContactTitle" or "Title" => 30,
            "CompanyName" or "ProductName" or "ShipName" => 40,
            "RegionDescription" or "TerritoryDescription" => 50,
            "Address" or "ShipAddress" => 60,
            "Extension" => 4,
            "Notes" => 2_048,
            "Description" or "CustomerDesc" or "PhotoPath" or "HomePage" => 255,
            _ => 255,
        };
        static FixtureIndex I(string name, bool unique, params string[] columns) => new(name, columns, unique);
        static FixtureTableSchema T(string name, FixtureColumn[] columns, string[] key, params FixtureIndex[] indexes)
        {
            var statistics = new List<FixtureStatistic>
            {
                new($"ST_{name.Replace(' ', '_')}_PK", key, "fullscan"),
            };
            statistics.AddRange(indexes.Select(index => new FixtureStatistic($"ST_{index.Name}", index.Columns, "fullscan")));
            return new(name, columns, key, indexes, statistics);
        }
        static FixtureForeignKey F(string name, string child, string[] childColumns, string parent, params string[] parentColumns) =>
            new(name, child, childColumns, parent, parentColumns);

        var tables = new[]
        {
            T("Categories", [C("CategoryID", "Int32", generated: true), C("CategoryName", "String"), C("Description", "String", true)], ["CategoryID"], I("IX_Categories_Name", false, "CategoryName")),
            T("Customers", [C("CustomerID", "String"), C("CompanyName", "String"), C("ContactName", "String", true), C("ContactTitle", "String", true), C("Address", "String", true), C("City", "String", true), C("Region", "String", true), C("PostalCode", "String", true), C("Country", "String", true), C("Phone", "String", true), C("Fax", "String", true)], ["CustomerID"], I("IX_Customers_City", false, "City"), I("IX_Customers_Company", false, "CompanyName")),
            T("CustomerCustomerDemo", [C("CustomerID", "String"), C("CustomerTypeID", "String")], ["CustomerID", "CustomerTypeID"]),
            T("CustomerDemographics", [C("CustomerTypeID", "String"), C("CustomerDesc", "String", true)], ["CustomerTypeID"]),
            T("Employees", [C("EmployeeID", "Int32", generated: true), C("LastName", "String"), C("FirstName", "String"), C("Title", "String", true), C("TitleOfCourtesy", "String", true), C("BirthDate", "DateTime", true), C("HireDate", "DateTime", true), C("Address", "String", true), C("City", "String", true), C("Region", "String", true), C("PostalCode", "String", true), C("Country", "String", true), C("HomePhone", "String", true), C("Extension", "String", true), C("Notes", "String", true), C("ReportsTo", "Int32", true), C("PhotoPath", "String", true)], ["EmployeeID"], I("IX_Employees_LastName", false, "LastName")),
            T("EmployeeTerritories", [C("EmployeeID", "Int32"), C("TerritoryID", "String")], ["EmployeeID", "TerritoryID"]),
            T("Orders", [C("OrderID", "Int32", generated: true), C("CustomerID", "String", true), C("EmployeeID", "Int32", true), C("OrderDate", "DateTime", true), C("RequiredDate", "DateTime", true), C("ShippedDate", "DateTime", true), C("ShipVia", "Int32", true), C("Freight", "Decimal", true), C("ShipName", "String", true), C("ShipAddress", "String", true), C("ShipCity", "String", true), C("ShipRegion", "String", true), C("ShipPostalCode", "String", true), C("ShipCountry", "String", true)], ["OrderID"], I("IX_Orders_Customer", false, "CustomerID"), I("IX_Orders_Date", false, "OrderDate")),
            T("Order Details", [C("OrderID", "Int32"), C("ProductID", "Int32"), C("UnitPrice", "Decimal"), C("Quantity", "Int16"), C("Discount", "Single")], ["OrderID", "ProductID"]),
            T("Products", [C("ProductID", "Int32", generated: true), C("ProductName", "String"), C("SupplierID", "Int32", true), C("CategoryID", "Int32", true), C("QuantityPerUnit", "String", true), C("UnitPrice", "Decimal", true), C("UnitsInStock", "Int16", true), C("UnitsOnOrder", "Int16", true), C("ReorderLevel", "Int16", true), C("Discontinued", "Boolean")], ["ProductID"], I("IX_Products_Name", false, "ProductName"), I("IX_Products_Category", false, "CategoryID")),
            T("Region", [C("RegionID", "Int32"), C("RegionDescription", "String")], ["RegionID"]),
            T("Shippers", [C("ShipperID", "Int32", generated: true), C("CompanyName", "String"), C("Phone", "String", true)], ["ShipperID"]),
            T("Suppliers", [C("SupplierID", "Int32", generated: true), C("CompanyName", "String"), C("ContactName", "String", true), C("ContactTitle", "String", true), C("Address", "String", true), C("City", "String", true), C("Region", "String", true), C("PostalCode", "String", true), C("Country", "String", true), C("Phone", "String", true), C("Fax", "String", true), C("HomePage", "String", true)], ["SupplierID"], I("IX_Suppliers_Company", false, "CompanyName")),
            T("Territories", [C("TerritoryID", "String"), C("TerritoryDescription", "String"), C("RegionID", "Int32")], ["TerritoryID"]),
        };

        var foreignKeys = new[]
        {
            F("FK_CustomerDemo_Customer", "CustomerCustomerDemo", ["CustomerID"], "Customers", "CustomerID"),
            F("FK_CustomerDemo_Type", "CustomerCustomerDemo", ["CustomerTypeID"], "CustomerDemographics", "CustomerTypeID"),
            F("FK_Employees_Manager", "Employees", ["ReportsTo"], "Employees", "EmployeeID"),
            F("FK_EmployeeTerritories_Employee", "EmployeeTerritories", ["EmployeeID"], "Employees", "EmployeeID"),
            F("FK_EmployeeTerritories_Territory", "EmployeeTerritories", ["TerritoryID"], "Territories", "TerritoryID"),
            F("FK_Orders_Customer", "Orders", ["CustomerID"], "Customers", "CustomerID"),
            F("FK_Orders_Employee", "Orders", ["EmployeeID"], "Employees", "EmployeeID"),
            F("FK_Orders_Shipper", "Orders", ["ShipVia"], "Shippers", "ShipperID"),
            F("FK_OrderDetails_Order", "Order Details", ["OrderID"], "Orders", "OrderID"),
            F("FK_OrderDetails_Product", "Order Details", ["ProductID"], "Products", "ProductID"),
            F("FK_Products_Supplier", "Products", ["SupplierID"], "Suppliers", "SupplierID"),
            F("FK_Products_Category", "Products", ["CategoryID"], "Categories", "CategoryID"),
            F("FK_Territories_Region", "Territories", ["RegionID"], "Region", "RegionID"),
        };

        return new FixtureSchema(ContractVersion, tables, foreignKeys);
    }
}
