using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Benchmarks.Ef;
using Inquiry.Northwind.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks;

/// <summary>
/// CRUD comparison for the string-PK <c>Customers</c> table. Operations: SelectAll,
/// SelectByKey, SelectByField (Country), Insert, Update, Upsert.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CustomerCrudBenchmarks
{
    private BenchmarkDatabase _db = null!;
    private string _connectionString = null!;

    // Fixed targets used by SelectByKey / Update / Upsert. "00000" is the first seeded row.
    private const string TargetCustomerId = "00000";
    private const string TargetCountry    = "USA";

    // Monotonic counter so Insert iterations never collide on PK.
    private int _insertCounter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = BenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // Read every column the Inquiry-mapped Customer entity reads, so AdoNet/Dapper do equal
    // per-row work to Inquiry (a fair comparison — not a hand-picked 5-of-11 subset).
    private const string SelectColumns =
        "CustomerID, CompanyName, ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax";

    private static Customer ReadCustomer(System.Data.Common.DbDataReader reader) => new Customer
    {
        CustomerID   = reader.GetString(0),
        CompanyName  = reader.GetString(1),
        ContactName  = reader.IsDBNull(2) ? null : reader.GetString(2),
        ContactTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
        Address      = reader.IsDBNull(4) ? null : reader.GetString(4),
        City         = reader.IsDBNull(5) ? null : reader.GetString(5),
        Region       = reader.IsDBNull(6) ? null : reader.GetString(6),
        PostalCode   = reader.IsDBNull(7) ? null : reader.GetString(7),
        Country      = reader.IsDBNull(8) ? null : reader.GetString(8),
        Phone        = reader.IsDBNull(9) ? null : reader.GetString(9),
        Fax          = reader.IsDBNull(10) ? null : reader.GetString(10),
    };

    // ---- SelectAll ----------------------------------------------------------------------

    [BenchmarkCategory("SelectAll"), Benchmark(Baseline = true)]
    public async Task<int> SelectAll_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Customers;";
        var list = new List<Customer>(BenchmarkDatabase.SeedRows);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadCustomer(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Customer>(
            $"SELECT {SelectColumns} FROM Customers;")).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Customers.AsNoTracking().ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Inquiry()
    {
        var list = await _db.Customers.SelectAllAsync();
        return list.Count;
    }

    // ---- SelectByKey --------------------------------------------------------------------

    [BenchmarkCategory("SelectByKey"), Benchmark(Baseline = true)]
    public async Task<Customer?> SelectByKey_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Customers WHERE CustomerID = $id;";
        command.Parameters.Add("$id", SqliteType.Text).Value = TargetCustomerId;
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadCustomer(reader) : null;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Customer?> SelectByKey_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Customer>(
            $"SELECT {SelectColumns} FROM Customers WHERE CustomerID = @id;",
            new { id = TargetCustomerId });
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<EfCustomer?> SelectByKey_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerID == TargetCustomerId);
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Customer?> SelectByKey_Inquiry()
        => await _db.Customers.SelectByKeyAsync(TargetCustomerId);

    // ---- SelectByField (Country) --------------------------------------------------------

    [BenchmarkCategory("SelectByField"), Benchmark(Baseline = true)]
    public async Task<int> SelectByField_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Customers WHERE Country = $c;";
        command.Parameters.Add("$c", SqliteType.Text).Value = TargetCountry;
        var list = new List<Customer>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(ReadCustomer(reader));
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<Customer>(
            $"SELECT {SelectColumns} FROM Customers WHERE Country = @c;",
            new { c = TargetCountry })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var list = await ctx.Customers.AsNoTracking().Where(c => c.Country == TargetCountry).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Inquiry()
    {
        var list = await _db.Customers.SelectByCountryAsync(TargetCountry);
        return list.Count;
    }

    // ---- Insert -------------------------------------------------------------------------

    private string NextInsertId() => BenchmarkDatabase.SeedCustomerId(
        BenchmarkDatabase.SeedRows + Interlocked.Increment(ref _insertCounter));

    [BenchmarkCategory("Insert"), Benchmark(Baseline = true)]
    public async Task<int> Insert_AdoNet()
    {
        var id = NextInsertId();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
            "VALUES ($id, $company, $contact, $country, $city);";
        command.Parameters.Add("$id",      SqliteType.Text).Value = id;
        command.Parameters.Add("$company", SqliteType.Text).Value = "Bench Co";
        command.Parameters.Add("$contact", SqliteType.Text).Value = "Bench Contact";
        command.Parameters.Add("$country", SqliteType.Text).Value = "USA";
        command.Parameters.Add("$city",    SqliteType.Text).Value = "Bench City";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Dapper()
    {
        var id = NextInsertId();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
            "VALUES (@id, @company, @contact, @country, @city);",
            new { id, company = "Bench Co", contact = "Bench Contact", country = "USA", city = "Bench City" });
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        ctx.Customers.Add(new EfCustomer
        {
            CustomerID  = NextInsertId(),
            CompanyName = "Bench Co",
            ContactName = "Bench Contact",
            Country     = "USA",
            City        = "Bench City",
        });
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<int> Insert_Inquiry()
        => await _db.Customers.InsertAsync(new Customer
        {
            CustomerID  = NextInsertId(),
            CompanyName = "Bench Co",
            ContactName = "Bench Contact",
            Country     = "USA",
            City        = "Bench City",
        });

    // ---- Update -------------------------------------------------------------------------

    [BenchmarkCategory("Update"), Benchmark(Baseline = true)]
    public async Task<int> Update_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Customers SET CompanyName = $company, ContactName = $contact, " +
            "Country = $country, City = $city WHERE CustomerID = $id;";
        command.Parameters.Add("$id",      SqliteType.Text).Value = TargetCustomerId;
        command.Parameters.Add("$company", SqliteType.Text).Value = "Updated Co";
        command.Parameters.Add("$contact", SqliteType.Text).Value = "Updated Contact";
        command.Parameters.Add("$country", SqliteType.Text).Value = "USA";
        command.Parameters.Add("$city",    SqliteType.Text).Value = "Updated City";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(
            "UPDATE Customers SET CompanyName = @company, ContactName = @contact, " +
            "Country = @country, City = @city WHERE CustomerID = @id;",
            new { id = TargetCustomerId, company = "Updated Co", contact = "Updated Contact", country = "USA", city = "Updated City" });
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<int> Update_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        var entity = await ctx.Customers.FirstAsync(c => c.CustomerID == TargetCustomerId);
        entity.CompanyName = "Updated Co";
        entity.ContactName = "Updated Contact";
        entity.Country     = "USA";
        entity.City        = "Updated City";
        return await ctx.SaveChangesAsync();
    }

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<bool> Update_Inquiry()
        => await _db.Customers.UpdateAsync(new Customer
        {
            CustomerID  = TargetCustomerId,
            CompanyName = "Updated Co",
            ContactName = "Updated Contact",
            Country     = "USA",
            City        = "Updated City",
        });

    // ---- Upsert (raw SQL for the non-Inquiry libraries) ---------------------------------

    private const string UpsertSql =
        "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
        "VALUES ($id, $company, $contact, $country, $city) " +
        "ON CONFLICT(CustomerID) DO UPDATE SET " +
        "    CompanyName = excluded.CompanyName, " +
        "    ContactName = excluded.ContactName, " +
        "    Country     = excluded.Country, " +
        "    City        = excluded.City;";

    private const string UpsertSqlAt =
        "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
        "VALUES (@id, @company, @contact, @country, @city) " +
        "ON CONFLICT(CustomerID) DO UPDATE SET " +
        "    CompanyName = excluded.CompanyName, " +
        "    ContactName = excluded.ContactName, " +
        "    Country     = excluded.Country, " +
        "    City        = excluded.City;";

    [BenchmarkCategory("Upsert"), Benchmark(Baseline = true)]
    public async Task<int> Upsert_AdoNet()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.Add("$id",      SqliteType.Text).Value = TargetCustomerId;
        command.Parameters.Add("$company", SqliteType.Text).Value = "Upserted Co";
        command.Parameters.Add("$contact", SqliteType.Text).Value = "Upserted Contact";
        command.Parameters.Add("$country", SqliteType.Text).Value = "USA";
        command.Parameters.Add("$city",    SqliteType.Text).Value = "Upserted City";
        return await command.ExecuteNonQueryAsync();
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Dapper()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(UpsertSqlAt, new
        {
            id      = TargetCustomerId,
            company = "Upserted Co",
            contact = "Upserted Contact",
            country = "USA",
            city    = "Upserted City",
        });
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_EfCore()
    {
        await using var ctx = await _db.DbContextFactory.CreateDbContextAsync();
        return await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO Customers (CustomerID, CompanyName, ContactName, Country, City) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}) " +
            "ON CONFLICT(CustomerID) DO UPDATE SET " +
            "    CompanyName = excluded.CompanyName, " +
            "    ContactName = excluded.ContactName, " +
            "    Country     = excluded.Country, " +
            "    City        = excluded.City;",
            TargetCustomerId, "Upserted Co", "Upserted Contact", "USA", "Upserted City");
    }

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<int> Upsert_Inquiry()
        => await _db.Customers.UpsertAsync(new Customer
        {
            CustomerID  = TargetCustomerId,
            CompanyName = "Upserted Co",
            ContactName = "Upserted Contact",
            Country     = "USA",
            City        = "Upserted City",
        });
}
