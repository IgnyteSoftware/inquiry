using Inquiry.Northwind;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

/// <summary>
/// Runs <c>EXPLAIN QUERY PLAN</c> for every distinct SQL statement used across the benchmark
/// suite and writes the output to a checked-in evidence file. Invoked via <c>--capture-plans</c>.
/// </summary>
internal static class QueryPlanCapture
{
    private const string ProductColumns =
        "ProductID, ProductName, SupplierID, CategoryID, QuantityPerUnit, UnitPrice, " +
        "UnitsInStock, UnitsOnOrder, ReorderLevel, Discontinued";
    private const string CustomerColumns =
        "CustomerID, CompanyName, ContactName, ContactTitle, Address, City, " +
        "Region, PostalCode, Country, Phone, Fax";

    private static readonly (string Category, string Label, string Sql)[] Statements =
    {
        // ---- ShipperCrud ---------------------------------------------------------------
        ("ShipperCrud", "SelectAll",
            "SELECT ShipperID, CompanyName, Phone FROM Shippers"),
        ("ShipperCrud", "SelectByKey",
            "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE ShipperID = $id"),
        ("ShipperCrud", "SelectByField",
            "SELECT ShipperID, CompanyName, Phone FROM Shippers WHERE CompanyName = $c"),
        ("ShipperCrud", "Insert",
            "INSERT INTO Shippers (CompanyName, Phone) VALUES ($company, $phone)"),
        ("ShipperCrud", "Update",
            "UPDATE Shippers SET CompanyName = $company, Phone = $phone WHERE ShipperID = $id"),
        ("ShipperCrud", "Upsert",
            "INSERT INTO Shippers (ShipperID, CompanyName, Phone) VALUES ($id, $company, $phone) " +
            "ON CONFLICT(ShipperID) DO UPDATE SET CompanyName = excluded.CompanyName, Phone = excluded.Phone"),

        // ---- CustomerCrud --------------------------------------------------------------
        ("CustomerCrud", "SelectAll",
            $"SELECT {CustomerColumns} FROM Customers"),
        ("CustomerCrud", "SelectByKey",
            $"SELECT {CustomerColumns} FROM Customers WHERE CustomerID = $id"),
        ("CustomerCrud", "SelectByField",
            $"SELECT {CustomerColumns} FROM Customers WHERE Country = $c"),
        ("CustomerCrud", "Insert",
            "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
            "VALUES ($id, $company, $contact, $country, $city)"),
        ("CustomerCrud", "Update",
            "UPDATE Customers SET CompanyName = $company, ContactName = $contact, " +
            "Country = $country, City = $city WHERE CustomerID = $id"),
        ("CustomerCrud", "Upsert",
            "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
            "VALUES ($id, $company, $contact, $country, $city) " +
            "ON CONFLICT(CustomerID) DO UPDATE SET " +
            "CompanyName = excluded.CompanyName, ContactName = excluded.ContactName, " +
            "Country = excluded.Country, City = excluded.City"),

        // ---- ProductCrud ---------------------------------------------------------------
        ("ProductCrud", "SelectAll",
            $"SELECT {ProductColumns} FROM Products"),
        ("ProductCrud", "SelectByKey",
            $"SELECT {ProductColumns} FROM Products WHERE ProductID = $id"),

        // ---- Pagination ----------------------------------------------------------------
        ("Pagination", "OffsetPage",
            $"SELECT {ProductColumns} FROM Products ORDER BY ProductID LIMIT $limit OFFSET $off"),
        ("Pagination", "KeysetPage",
            $"SELECT {ProductColumns} FROM Products WHERE ProductID > $after ORDER BY ProductID LIMIT $limit"),

        // ---- Predicates ----------------------------------------------------------------
        ("Predicate", "Search",
            $"SELECT {ProductColumns} FROM Products WHERE UnitPrice >= $min AND ProductName LIKE $pattern"),
        ("Predicate", "InList (3 elements)",
            $"SELECT {ProductColumns} FROM Products WHERE CategoryID IN ($c0, $c1, $c2)"),

        // ---- Projection & Aggregates ---------------------------------------------------
        ("Aggregate", "Projection",
            "SELECT ProductID, ProductName, UnitPrice FROM Products"),
        ("Aggregate", "Count",
            "SELECT COUNT(*) FROM Products"),
        ("Aggregate", "Sum",
            "SELECT SUM(UnitPrice) FROM Products"),
        ("Aggregate", "Avg",
            "SELECT AVG(UnitPrice) FROM Products"),
        ("Aggregate", "Min",
            "SELECT MIN(UnitPrice) FROM Products"),
        ("Aggregate", "Max",
            "SELECT MAX(UnitPrice) FROM Products"),

        // ---- Eager Loading -------------------------------------------------------------
        ("EagerLoading", "Categories (related query)",
            "SELECT CategoryID, CategoryName, Description FROM Categories"),

        // ---- Batch / Transaction -------------------------------------------------------
        ("Batch", "Insert Region",
            "INSERT INTO Region (RegionID, RegionDescription) VALUES ($id, $desc)"),

        // ---- IN Cardinality (representative sizes) -------------------------------------
        ("InCardinality", "IN 10 elements",
            $"SELECT {ProductColumns} FROM Products WHERE CategoryID IN " +
            $"({string.Join(",", Enumerable.Range(0, 10).Select(i => "$c" + i))})"),
        ("InCardinality", "IN 100 elements",
            $"SELECT {ProductColumns} FROM Products WHERE CategoryID IN " +
            $"({string.Join(",", Enumerable.Range(0, 100).Select(i => "$c" + i))})"),
    };

    public static async Task RunAsync()
    {
        Console.WriteLine("Creating benchmark database...");
        var dbPath = Path.Combine(Path.GetTempPath(), $"inquiry_plans_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var ddl = connection.CreateCommand();
                ddl.CommandText = NorthwindSchema.SqliteDdl;
                await ddl.ExecuteNonQueryAsync();
            }

            var repoRoot = FindRepoRoot();
            var outputDir = Path.Combine(repoRoot, "benchmarks", "evidence", "query-plans");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var outputPath = Path.Combine(outputDir, "sqlite-query-plans.md");

            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();

            using var writer = new StreamWriter(outputPath);
            await writer.WriteLineAsync("# SQLite Query Plans");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(
                "Generated by `dotnet run --project benchmarks/Inquiry.Benchmarks -c Release -- --capture-plans`.");
            await writer.WriteLineAsync(
                "Each section shows `EXPLAIN QUERY PLAN` output for the SQL statements used in the benchmark suite.");
            await writer.WriteLineAsync();

            string? currentCategory = null;
            int count = 0;

            foreach (var (category, label, sql) in Statements)
            {
                if (category != currentCategory)
                {
                    currentCategory = category;
                    await writer.WriteLineAsync($"## {category}");
                    await writer.WriteLineAsync();
                }

                await writer.WriteLineAsync($"### {label}");
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("```sql");
                await writer.WriteLineAsync(sql);
                await writer.WriteLineAsync("```");
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("```");

                try
                {
                    await using var command = conn.CreateCommand();
                    command.CommandText = "EXPLAIN QUERY PLAN " + sql;
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var detail = reader.GetString(reader.GetOrdinal("detail"));
                        await writer.WriteLineAsync(detail);
                    }
                }
                catch (SqliteException ex)
                {
                    await writer.WriteLineAsync($"-- error: {ex.Message}");
                }

                await writer.WriteLineAsync("```");
                await writer.WriteLineAsync();
                count++;
            }

            Console.WriteLine($"Captured {count} query plans to: {outputPath}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
