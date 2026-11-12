using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Entities;
using Inquiry.Stores;
using Inquiry.Transactions;
using MySqlConnector;

namespace Inquiry.Benchmarks.MySql;

[InquiryTable("BenchmarkM2MOrder")]
public sealed class BenchmarkM2MOrder
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn(Length = 200)] public string Name { get; set; } = string.Empty;
    [InquiryManyToMany(typeof(BenchmarkM2MOrderProduct), nameof(BenchmarkM2MOrderProduct.OrderId), nameof(BenchmarkM2MOrderProduct.ProductId))]
    public List<BenchmarkM2MProduct> Products { get; set; } = new();
}

[InquiryTable("BenchmarkM2MProduct")]
public sealed class BenchmarkM2MProduct
{
    [InquiryKey] public long Id { get; set; }
    [InquiryColumn(Length = 200)] public string Title { get; set; } = string.Empty;
}

[InquiryTable("BenchmarkM2MOrderProduct")]
public sealed class BenchmarkM2MOrderProduct
{
    [InquiryKey] public long OrderId { get; set; }
    [InquiryKey] public long ProductId { get; set; }
}

public partial class BenchmarkM2MOrderStore : InquiryStore<BenchmarkM2MOrder>
{
    [InquirySelectAllEager]
    public partial IAsyncEnumerable<BenchmarkM2MOrder> AllWithProductsAsync(CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<BenchmarkM2MOrder?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Many-to-many eager reads through a junction table on MySQL: every order with its products
/// (<c>AllParents</c>) and one order with its products (<c>OneParent</c>). The ADO.NET and Dapper legs
/// send the same multi-result command as the generated store and assemble the graph the same way.
/// Requires Docker.
/// </summary>
/// <remarks>
/// EF Core, linq2db and RepoDB have no legs: this project has no many-to-many mapping for them.
/// The container is shared by the whole process. The first setup seeds the tables, which no other
/// class uses.
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ManyToManyBenchmarks
{
    private const int OrderCount = 100;
    private const int ProductCount = 1000;
    private const int ProductsPerOrder = 10;

    // The result sets come in the order that the generated store reads them: products, links, orders.
    private const string AllParentsSql =
        "SELECT `Id`, `Title` FROM `BenchmarkM2MProduct` WHERE `Id` IN (SELECT `ProductId` FROM `BenchmarkM2MOrderProduct` WHERE `OrderId` IN (SELECT `Id` FROM `BenchmarkM2MOrder`));" +
        "SELECT `OrderId`, `ProductId` FROM `BenchmarkM2MOrderProduct` WHERE `OrderId` IN (SELECT `Id` FROM `BenchmarkM2MOrder`) AND `ProductId` IN (SELECT `Id` FROM `BenchmarkM2MProduct`);" +
        "SELECT `Id`, `Name` FROM `BenchmarkM2MOrder`";
    private const string OneParentSql =
        "SELECT `Id`, `Name` FROM `BenchmarkM2MOrder` WHERE `Id` = @id;" +
        "SELECT p.`Id`, p.`Title` FROM `BenchmarkM2MProduct` p INNER JOIN `BenchmarkM2MOrderProduct` j ON j.`ProductId` = p.`Id` WHERE j.`OrderId` = @id";

    private MySqlBenchmarkDatabase _db = null!;
    private string _connectionString = null!;
    private long _targetOrderId;

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
        await using var connection = new MySqlConnection(_connectionString);
        if (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM `BenchmarkM2MOrderProduct`") == 0)
        {
            await _db.Inquiry.ExecuteInTransactionAsync(SeedAsync);
        }

        // A wrong leg would otherwise only show up as a suspiciously fast result.
        var links = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM `BenchmarkM2MOrderProduct`");
        int[] all = [await AllParents_AdoNet(), await AllParents_Dapper(), await AllParents_Inquiry()];
        if (links == 0 || all.Any(attached => attached != links))
        {
            throw new InvalidOperationException($"AllParents legs attached [{string.Join(", ", all)}] products. Expected {links} each.");
        }

        _targetOrderId = await connection.ExecuteScalarAsync<long>("SELECT MIN(`OrderId`) FROM `BenchmarkM2MOrderProduct`");
        int[] one = [await OneParent_AdoNet(), await OneParent_Dapper(), await OneParent_Inquiry()];
        if (one[0] <= 0 || one.Any(attached => attached != one[0]))
        {
            throw new InvalidOperationException($"OneParent legs attached [{string.Join(", ", one)}] products.");
        }
    }

    private static async Task SeedAsync(IInquiryTransaction transaction)
    {
        for (var id = 1L; id <= ProductCount; id++)
        {
            await transaction.ExecuteAsync($"INSERT INTO `BenchmarkM2MProduct` (`Id`, `Title`) VALUES ({id}, {"Product " + id})");
        }

        for (var id = 1L; id <= OrderCount; id++)
        {
            await transaction.ExecuteAsync($"INSERT INTO `BenchmarkM2MOrder` (`Id`, `Name`) VALUES ({id}, {"Order " + id})");
            for (var link = 0; link < ProductsPerOrder; link++)
            {
                // Half of the products get no link, so the product query must filter them out.
                var productId = ((id * 7 + link * 13) % (ProductCount / 2)) + 1;
                await transaction.ExecuteAsync($"INSERT INTO `BenchmarkM2MOrderProduct` (`OrderId`, `ProductId`) VALUES ({id}, {productId})");
            }
        }
    }

    // ---- AllParents ---------------------------------------------------------------------

    [BenchmarkCategory("AllParents"), Benchmark(Baseline = true)]
    public async Task<int> AllParents_AdoNet()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = AllParentsSql;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

        var products = new Dictionary<long, BenchmarkM2MProduct>();
        while (await reader.ReadAsync())
        {
            var product = new BenchmarkM2MProduct { Id = reader.GetInt64(0), Title = reader.GetString(1) };
            products[product.Id] = product;
        }

        await reader.NextResultAsync();
        var grouped = new Dictionary<long, List<BenchmarkM2MProduct>>();
        while (await reader.ReadAsync())
        {
            var orderId = reader.GetInt64(0);
            if (!products.TryGetValue(reader.GetInt64(1), out var product))
            {
                continue;
            }

            if (!grouped.TryGetValue(orderId, out var group))
            {
                grouped[orderId] = group = new List<BenchmarkM2MProduct>();
            }

            group.Add(product);
        }

        await reader.NextResultAsync();
        var attached = 0;
        while (await reader.ReadAsync())
        {
            var order = new BenchmarkM2MOrder { Id = reader.GetInt64(0), Name = reader.GetString(1) };
            order.Products = grouped.TryGetValue(order.Id, out var related) ? related : new List<BenchmarkM2MProduct>();
            attached += order.Products.Count;
        }

        return attached;
    }

    [BenchmarkCategory("AllParents"), Benchmark]
    public async Task<int> AllParents_Dapper()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var grid = await connection.QueryMultipleAsync(AllParentsSql);

        var products = (await grid.ReadAsync<BenchmarkM2MProduct>()).ToDictionary(product => product.Id);
        var grouped = new Dictionary<long, List<BenchmarkM2MProduct>>();
        foreach (var link in await grid.ReadAsync<BenchmarkM2MOrderProduct>())
        {
            if (!products.TryGetValue(link.ProductId, out var product))
            {
                continue;
            }

            if (!grouped.TryGetValue(link.OrderId, out var group))
            {
                grouped[link.OrderId] = group = new List<BenchmarkM2MProduct>();
            }

            group.Add(product);
        }

        var attached = 0;
        foreach (var order in await grid.ReadAsync<BenchmarkM2MOrder>())
        {
            order.Products = grouped.TryGetValue(order.Id, out var related) ? related : new List<BenchmarkM2MProduct>();
            attached += order.Products.Count;
        }

        return attached;
    }

    [BenchmarkCategory("AllParents"), Benchmark]
    public async Task<int> AllParents_Inquiry()
    {
        var attached = 0;
        await foreach (var order in _db.ManyToManyOrders.AllWithProductsAsync())
        {
            attached += order.Products.Count;
        }

        return attached;
    }

    // ---- OneParent ----------------------------------------------------------------------

    [BenchmarkCategory("OneParent"), Benchmark(Baseline = true)]
    public async Task<int> OneParent_AdoNet()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = OneParentSql;
        command.Parameters.AddWithValue("id", _targetOrderId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (!await reader.ReadAsync())
        {
            return -1;
        }

        var order = new BenchmarkM2MOrder { Id = reader.GetInt64(0), Name = reader.GetString(1) };
        await reader.NextResultAsync();
        while (await reader.ReadAsync())
        {
            order.Products.Add(new BenchmarkM2MProduct { Id = reader.GetInt64(0), Title = reader.GetString(1) });
        }

        return order.Products.Count;
    }

    [BenchmarkCategory("OneParent"), Benchmark]
    public async Task<int> OneParent_Dapper()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var grid = await connection.QueryMultipleAsync(OneParentSql, new { id = _targetOrderId });
        var order = await grid.ReadSingleOrDefaultAsync<BenchmarkM2MOrder>();
        if (order is null)
        {
            return -1;
        }

        order.Products = (await grid.ReadAsync<BenchmarkM2MProduct>()).AsList();
        return order.Products.Count;
    }

    [BenchmarkCategory("OneParent"), Benchmark]
    public async Task<int> OneParent_Inquiry()
    {
        var order = await _db.ManyToManyOrders.GetWithProductsAsync(_targetOrderId);
        return order?.Products.Count ?? -1;
    }
}
