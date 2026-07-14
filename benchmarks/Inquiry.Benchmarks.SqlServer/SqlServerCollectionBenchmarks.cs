using System.Data;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Configs;
using Inquiry.Commands;
using Inquiry.SqlServer.Parameters;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class SqlServerCollectionBenchmarks
{
    private static readonly InquiryTvpDescriptor IntTvpDescriptor =
        InquiryTvpDescriptor.Get("int", 0, 10, 0, false);

    internal const string Projection = """
        p.ProductID, p.ProductName, p.SupplierID, p.CategoryID, p.QuantityPerUnit,
        p.UnitPrice, p.UnitsInStock, p.UnitsOnOrder, p.ReorderLevel, p.Discontinued
        """;
    internal const string TvpSql = """
        /* inquiry-collection:tvp */
        SELECT p.ProductID, p.ProductName, p.SupplierID, p.CategoryID, p.QuantityPerUnit,
               p.UnitPrice, p.UnitsInStock, p.UnitsOnOrder, p.ReorderLevel, p.Discontinued
        FROM Products AS p
        WHERE EXISTS (SELECT 1 FROM @ids AS i WHERE i.Value = p.ProductID)
        ORDER BY p.ProductID;
        """;
    internal const string OpenJsonSql = """
        /* inquiry-collection:openjson */
        SELECT p.ProductID, p.ProductName, p.SupplierID, p.CategoryID, p.QuantityPerUnit,
               p.UnitPrice, p.UnitsInStock, p.UnitsOnOrder, p.ReorderLevel, p.Discontinued
        FROM Products AS p
        WHERE EXISTS (SELECT 1 FROM OPENJSON(@ids) WITH ([Value] int '$') AS i WHERE i.Value = p.ProductID)
        ORDER BY p.ProductID;
        """;

    private readonly int[] _one = CreateIds(1);
    private readonly int[] _ten = CreateIds(10);
    private readonly int[] _hundred = CreateIds(100);
    private readonly int[] _thousand = CreateIds(1_000);
    private SqlServerCollectionBenchmarkDatabase _database = null!;

    [GlobalSetup]
    public void Setup()
    {
        _database = SqlServerCollectionBenchmarkDatabase.CreateAsync().GetAwaiter().GetResult();
        SqlServerCollectionCorrectness.VerifyAsync(_database).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [BenchmarkCategory("N1"), Benchmark(Baseline = true)] public Task<List<ProductResult>> Tvp1() => ExecuteTvpAsync(_one);
    [BenchmarkCategory("N10"), Benchmark(Baseline = true)] public Task<List<ProductResult>> Tvp10() => ExecuteTvpAsync(_ten);
    [BenchmarkCategory("N100"), Benchmark(Baseline = true)] public Task<List<ProductResult>> Tvp100() => ExecuteTvpAsync(_hundred);
    [BenchmarkCategory("N1000"), Benchmark(Baseline = true)] public Task<List<ProductResult>> Tvp1000() => ExecuteTvpAsync(_thousand);
    [BenchmarkCategory("N1"), Benchmark] public Task<List<ProductResult>> OpenJson1() => ExecuteOpenJsonAsync(_one);
    [BenchmarkCategory("N10"), Benchmark] public Task<List<ProductResult>> OpenJson10() => ExecuteOpenJsonAsync(_ten);
    [BenchmarkCategory("N100"), Benchmark] public Task<List<ProductResult>> OpenJson100() => ExecuteOpenJsonAsync(_hundred);
    [BenchmarkCategory("N1000"), Benchmark] public Task<List<ProductResult>> OpenJson1000() => ExecuteOpenJsonAsync(_thousand);
    [BenchmarkCategory("N1"), Benchmark] public Task<List<ProductResult>> Scalar1() => ExecuteScalarAsync(_one);
    [BenchmarkCategory("N10"), Benchmark] public Task<List<ProductResult>> Scalar10() => ExecuteScalarAsync(_ten);
    [BenchmarkCategory("N100"), Benchmark] public Task<List<ProductResult>> Scalar100() => ExecuteScalarAsync(_hundred);
    [BenchmarkCategory("N1000"), Benchmark] public Task<List<ProductResult>> Scalar1000() => ExecuteScalarAsync(_thousand);

    internal Task<List<ProductResult>> ExecuteTvpAsync(int[]? ids)
        => ExecuteTvpAsync(_database.ConnectionString, _database.TvpTypeName, ids);
    internal Task<List<ProductResult>> ExecuteOpenJsonAsync(int[]? ids)
        => ExecuteOpenJsonAsync(_database.ConnectionString, ids);
    internal Task<List<ProductResult>> ExecuteScalarAsync(int[]? ids)
        => ExecuteScalarAsync(_database.ConnectionString, ids);

    internal static async Task<List<ProductResult>> ExecuteTvpAsync(string connectionString, string typeName, int[]? ids)
    {
        if (ids is null || ids.Length == 0) return [];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = TvpSql;
        return await ExecuteTvpCommandAsync(command, typeName, ids).ConfigureAwait(false);
    }

    internal static async Task<List<ProductResult>> ExecuteTvpCommandAsync(
        SqlCommand command,
        string typeName,
        IEnumerable<int> ids)
    {
        try
        {
            InquiryTvpParameter.Bind(command, "@ids", ids, typeName, IntTvpDescriptor);
            return await ReadAsync(command).ConfigureAwait(false);
        }
        finally
        {
            InquiryCommandResources.Dispose(command);
        }
    }

    internal static async Task<List<ProductResult>> ExecuteOpenJsonAsync(string connectionString, int[]? ids)
    {
        if (ids is null || ids.Length == 0) return [];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = OpenJsonSql;
        command.Parameters.Add("@ids", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(ids);
        return await ReadAsync(command).ConfigureAwait(false);
    }

    internal static async Task<List<ProductResult>> ExecuteScalarAsync(string connectionString, int[]? ids)
    {
        if (ids is null || ids.Length == 0) return [];
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var names = new string[ids.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            names[index] = "@p" + index;
            command.Parameters.Add(names[index], SqlDbType.Int).Value = ids[index];
        }
        command.CommandText = ScalarSql(names);
        return await ReadAsync(command).ConfigureAwait(false);
    }

    internal static string ScalarSql(int cardinality)
        => ScalarSql(Enumerable.Range(0, cardinality).Select(static index => "@p" + index).ToArray());

    private static string ScalarSql(string[] parameterNames) => $"""
        /* inquiry-collection:scalar:n{parameterNames.Length} */
        SELECT {Projection}
        FROM Products AS p
        WHERE p.ProductID IN ({string.Join(",", parameterNames)})
        ORDER BY p.ProductID;
        """;

    private static async Task<List<ProductResult>> ReadAsync(SqlCommand command)
    {
        var products = new List<ProductResult>();
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleResult | CommandBehavior.SequentialAccess).ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            products.Add(new ProductResult(
                reader.GetInt32(0), reader.GetString(1), GetNullableInt32(reader, 2), GetNullableInt32(reader, 3),
                GetNullableString(reader, 4), GetNullableDecimal(reader, 5), GetNullableInt16(reader, 6),
                GetNullableInt16(reader, 7), GetNullableInt16(reader, 8), reader.GetBoolean(9)));
        }
        return products;
    }

    internal static int[] CreateIds(int cardinality) => Enumerable.Range(1, cardinality).ToArray();
    private static int? GetNullableInt32(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static short? GetNullableInt16(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt16(ordinal);
    private static decimal? GetNullableDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static string? GetNullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record ProductResult(
    int ProductId, string ProductName, int? SupplierId, int? CategoryId, string? QuantityPerUnit,
    decimal? UnitPrice, short? UnitsInStock, short? UnitsOnOrder, short? ReorderLevel, bool Discontinued);

public static class SqlServerCollectionCorrectness
{
    public static async Task VerifyAsync(SqlServerCollectionBenchmarkDatabase database)
    {
        foreach (var cardinality in new[] { 1, 10, 100, 1_000 })
        {
            var ids = SqlServerCollectionBenchmarks.CreateIds(cardinality);
            var tvp = await SqlServerCollectionBenchmarks.ExecuteTvpAsync(database.ConnectionString, database.TvpTypeName, ids).ConfigureAwait(false);
            var json = await SqlServerCollectionBenchmarks.ExecuteOpenJsonAsync(database.ConnectionString, ids).ConfigureAwait(false);
            var scalar = await SqlServerCollectionBenchmarks.ExecuteScalarAsync(database.ConnectionString, ids).ConfigureAwait(false);
            if (tvp.Count != cardinality || !tvp.SequenceEqual(json) || !tvp.SequenceEqual(scalar) ||
                !tvp.Select(static product => product.ProductId).SequenceEqual(ids))
                throw new InvalidOperationException($"Collection transport parity failed at cardinality {cardinality}.");
        }

        var empty = Array.Empty<int>();
        if ((await SqlServerCollectionBenchmarks.ExecuteTvpAsync(database.ConnectionString, database.TvpTypeName, null).ConfigureAwait(false)).Count != 0 ||
            (await SqlServerCollectionBenchmarks.ExecuteTvpAsync(database.ConnectionString, database.TvpTypeName, empty).ConfigureAwait(false)).Count != 0 ||
            (await SqlServerCollectionBenchmarks.ExecuteOpenJsonAsync(database.ConnectionString, null).ConfigureAwait(false)).Count != 0 ||
            (await SqlServerCollectionBenchmarks.ExecuteOpenJsonAsync(database.ConnectionString, empty).ConfigureAwait(false)).Count != 0 ||
            (await SqlServerCollectionBenchmarks.ExecuteScalarAsync(database.ConnectionString, null).ConfigureAwait(false)).Count != 0 ||
            (await SqlServerCollectionBenchmarks.ExecuteScalarAsync(database.ConnectionString, empty).ConfigureAwait(false)).Count != 0)
            throw new InvalidOperationException("Null and empty collection semantics must return zero rows.");

        var duplicates = new[] { 1, 1, 2, 2 };
        var expected = new[] { 1, 2 };
        foreach (var rows in new[]
        {
            await SqlServerCollectionBenchmarks.ExecuteTvpAsync(database.ConnectionString, database.TvpTypeName, duplicates).ConfigureAwait(false),
            await SqlServerCollectionBenchmarks.ExecuteOpenJsonAsync(database.ConnectionString, duplicates).ConfigureAwait(false),
            await SqlServerCollectionBenchmarks.ExecuteScalarAsync(database.ConnectionString, duplicates).ConfigureAwait(false),
        })
            if (!rows.Select(static product => product.ProductId).SequenceEqual(expected))
                throw new InvalidOperationException("Duplicate collection values must produce unique product rows.");
    }
}
