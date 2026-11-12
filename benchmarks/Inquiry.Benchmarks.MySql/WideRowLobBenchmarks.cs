using System.Data;
using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Northwind.Models;
using MySqlConnector;

namespace Inquiry.Benchmarks.MySql;

/// <summary>
/// Wide rows with a large-object column on MySQL: a full read and a point read of the 17 mapped
/// <c>Employees</c> columns, where <c>Notes</c> holds <see cref="NotesLength"/> characters. At 256
/// characters the row is wide but small. At 65 536 characters the <c>Notes</c> value dominates the row.
/// Requires Docker.
/// </summary>
/// <remarks>
/// EF Core, linq2db and RepoDB have no legs: this project maps only shippers for them. The container
/// is shared by the whole process. Each setup deletes and reseeds <c>Employees</c>, which no other
/// class reads.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WideRowLobBenchmarks
{
    private const int RowCount = 100;
    private const string SelectAllSql =
        "SELECT `EmployeeID`, `LastName`, `FirstName`, `Title`, `TitleOfCourtesy`, `BirthDate`, `HireDate`, `Address`, `City`, `Region`, " +
        "`PostalCode`, `Country`, `HomePhone`, `Extension`, `Notes`, `ReportsTo`, `PhotoPath` FROM `Employees`";
    private const string SelectByKeySql = SelectAllSql + " WHERE `EmployeeID` = @id";
    private const string AotJustification = "The AOT leg must do the same work as the plain Dapper leg. Only the interception differs.";

    private MySqlBenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private int _targetEmployeeId;

    [Params(256, 65536)] public int NotesLength { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = MySqlBenchmarkDatabase.CreateAsync(1000).GetAwaiter().GetResult();
        _connectionString = _db.ConnectionString;
        SeedAndVerifyAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task SeedAndVerifyAsync()
    {
        await _db.Inquiry.ExecuteAsync($"DELETE FROM `Employees`");
        for (var i = 0; i < RowCount; i++)
        {
            await _db.Employees.InsertAsync(new Employee
            {
                LastName = $"Last {i}",
                FirstName = $"First {i}",
                Title = "Sales Representative",
                TitleOfCourtesy = "Dr.",
                BirthDate = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(i),
                HireDate = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(i),
                Address = $"{i} Benchmark Street",
                City = $"City {i % 10}",
                Region = "WA",
                PostalCode = $"{98000 + i}",
                Country = "USA",
                HomePhone = $"555-{i:0000}",
                Extension = $"{1000 + i}",
                Notes = new string((char)('a' + (i % 26)), NotesLength),
                PhotoPath = $"photos/{i}.bmp",
            });
        }

        // A wrong leg would otherwise only show up as a suspiciously fast result.
        var expected = await WideList_AdoNet();
        long[] totals = [expected, await WideList_Dapper(), await WideList_DapperAot(), await WideList_Inquiry()];
        if (expected != (long)RowCount * NotesLength || totals.Any(total => total != expected))
        {
            throw new InvalidOperationException($"WideList legs read [{string.Join(", ", totals)}] Notes characters. Expected {(long)RowCount * NotesLength} each.");
        }

        await using (var connection = new MySqlConnection(_connectionString))
        {
            _targetEmployeeId = (await connection.QueryAsync<int>("SELECT MIN(`EmployeeID`) FROM `Employees`")).Single();
        }

        var row = Describe(await WideByKey_AdoNet());
        string[] rows = [row, Describe(await WideByKey_Dapper()), Describe(await WideByKey_DapperAot()), Describe(await WideByKey_Inquiry())];
        if (row.Length == 0 || rows.Any(other => other != row))
        {
            throw new InvalidOperationException($"WideByKey legs disagree: [{string.Join(" | ", rows)}].");
        }

        static string Describe(Employee? e) => e is null
            ? string.Empty
            : $"{e.EmployeeID} {e.LastName} {e.FirstName} {e.BirthDate:O} {e.HireDate:O} {e.PostalCode} {e.Notes?.Length} {e.ReportsTo} {e.PhotoPath}";
    }

    private static Employee ReadEmployee(System.Data.Common.DbDataReader reader) => new Employee
    {
        EmployeeID = reader.GetInt32(0),
        LastName = reader.GetString(1),
        FirstName = reader.GetString(2),
        Title = reader.IsDBNull(3) ? null : reader.GetString(3),
        TitleOfCourtesy = reader.IsDBNull(4) ? null : reader.GetString(4),
        BirthDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
        HireDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        Address = reader.IsDBNull(7) ? null : reader.GetString(7),
        City = reader.IsDBNull(8) ? null : reader.GetString(8),
        Region = reader.IsDBNull(9) ? null : reader.GetString(9),
        PostalCode = reader.IsDBNull(10) ? null : reader.GetString(10),
        Country = reader.IsDBNull(11) ? null : reader.GetString(11),
        HomePhone = reader.IsDBNull(12) ? null : reader.GetString(12),
        Extension = reader.IsDBNull(13) ? null : reader.GetString(13),
        Notes = reader.IsDBNull(14) ? null : reader.GetString(14),
        ReportsTo = reader.IsDBNull(15) ? null : reader.GetInt32(15),
        PhotoPath = reader.IsDBNull(16) ? null : reader.GetString(16),
    };

    private static long NotesCharacters(List<Employee> employees) => employees.Sum(e => (long)(e.Notes?.Length ?? 0));

    // ---- WideList -----------------------------------------------------------------------

    [BenchmarkCategory("WideList"), Benchmark(Baseline = true)]
    public async Task<long> WideList_AdoNet()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectAllSql;
        var list = new List<Employee>(RowCount);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            list.Add(ReadEmployee(reader));
        }

        return NotesCharacters(list);
    }

    [BenchmarkCategory("WideList"), Benchmark]
    public async Task<long> WideList_Dapper()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return NotesCharacters((await connection.QueryAsync<Employee>(SelectAllSql)).AsList());
    }

    [BenchmarkCategory("WideList"), Benchmark]
    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    public async Task<long> WideList_DapperAot()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return NotesCharacters((await connection.QueryAsync<Employee>(SelectAllSql)).AsList());
    }

    [BenchmarkCategory("WideList"), Benchmark]
    public async Task<long> WideList_Inquiry()
    {
        var list = new List<Employee>(RowCount);
        await foreach (var employee in _db.Employees.SelectAllAsync())
        {
            list.Add(employee);
        }

        return NotesCharacters(list);
    }

    // ---- WideByKey ----------------------------------------------------------------------

    [BenchmarkCategory("WideByKey"), Benchmark(Baseline = true)]
    public async Task<Employee?> WideByKey_AdoNet()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = SelectByKeySql;
        command.Parameters.AddWithValue("id", _targetEmployeeId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow | CommandBehavior.SequentialAccess);
        return await reader.ReadAsync() ? ReadEmployee(reader) : null;
    }

    [BenchmarkCategory("WideByKey"), Benchmark]
    public async Task<Employee?> WideByKey_Dapper()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Employee>(SelectByKeySql, new { id = _targetEmployeeId });
    }

    [BenchmarkCategory("WideByKey"), Benchmark]
    [DapperAot]
    [SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = AotJustification)]
    public async Task<Employee?> WideByKey_DapperAot()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Employee>(SelectByKeySql, new { id = _targetEmployeeId });
    }

    [BenchmarkCategory("WideByKey"), Benchmark]
    public async Task<Employee?> WideByKey_Inquiry()
        => await _db.Employees.SelectByKeyAsync(_targetEmployeeId);
}
