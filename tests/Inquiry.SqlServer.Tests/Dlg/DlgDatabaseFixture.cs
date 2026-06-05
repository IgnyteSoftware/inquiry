using Inquiry.Benchmarks.SqlServer.Dlg;
using Inquiry.Northwind;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

/// <summary>
/// One SQL Server container + one Northwind database with DLG's stored procedures applied and a
/// known seed, shared by all DLG smoke tests. DLG's config is process-static, so a single primed
/// connection string serves every test — hence one shared database here.
/// </summary>
public sealed class DlgDatabaseFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public const int SeededShippers = 3;
    public const int SeededProducts = 5;

    /// <summary>Category id → number of products seeded under it (for the eager assertion).</summary>
    public IReadOnlyDictionary<int, int> ProductCountByCategoryId { get; private set; } =
        new Dictionary<int, int>();

    public int FirstCategoryId { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
            var cs = _container.GetConnectionString();

            await ApplySchemaAsync(cs);
            await DlgSetup.ApplyStoredProceduresAsync(cs);
            await SeedAsync(cs);
            DlgSetup.PrimeConfig(cs);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    private static async Task ApplySchemaAsync(string cs)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = NorthwindSchema.SqlServerDdl;
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(string cs)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        for (int i = 0; i < SeededShippers; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@c, @p);";
            cmd.Parameters.AddWithValue("@c", $"Shipper {i}");
            cmd.Parameters.AddWithValue("@p", $"555-{i:0000}");
            await cmd.ExecuteNonQueryAsync();
        }

        // Two categories; all seeded products go to the first (clean eager assertion).
        var categoryIds = new List<int>();
        for (int i = 0; i < 2; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Categories (CategoryName, Description) OUTPUT inserted.CategoryID VALUES (@n, @d);";
            cmd.Parameters.AddWithValue("@n", $"Category {i}");
            cmd.Parameters.AddWithValue("@d", $"Desc {i}");
            categoryIds.Add((int)(await cmd.ExecuteScalarAsync())!);
        }
        FirstCategoryId = categoryIds[0];

        for (int i = 0; i < SeededProducts; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Products (ProductName, CategoryID, UnitPrice, Discontinued) VALUES (@n, @cat, @price, 0);";
            cmd.Parameters.AddWithValue("@n", $"Product {i}");
            cmd.Parameters.AddWithValue("@cat", categoryIds[0]);
            cmd.Parameters.AddWithValue("@price", 10m + i);
            await cmd.ExecuteNonQueryAsync();
        }

        ProductCountByCategoryId = new Dictionary<int, int>
        {
            [categoryIds[0]] = SeededProducts,
            [categoryIds[1]] = 0,
        };
    }
}
