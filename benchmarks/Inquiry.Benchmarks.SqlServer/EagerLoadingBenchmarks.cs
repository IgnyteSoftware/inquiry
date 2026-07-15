using System.Data;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Inquiry.Entities;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;
using DlgLib = global::Inquiry.Benchmarks.DLG;

namespace Inquiry.Benchmarks.SqlServer;

[InquiryTable("BenchmarkM2MOrder")]
public sealed class BenchmarkM2MOrder
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = string.Empty;
    [InquiryManyToMany(typeof(BenchmarkM2MOrderProduct), nameof(BenchmarkM2MOrderProduct.OrderId), nameof(BenchmarkM2MOrderProduct.ProductId))]
    public List<BenchmarkM2MProduct> Products { get; set; } = new();
}

[InquiryTable("BenchmarkM2MProduct")]
public sealed class BenchmarkM2MProduct
{
    [InquiryKey(IsGenerated = true)] public long Id { get; set; }
    [InquiryColumn] public string Title { get; set; } = string.Empty;
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
}

/// <summary>
/// Eager parent-with-children: load one <c>Category</c> together with its <c>Products</c> in a single
/// round-trip — the shape DLG supports natively (<c>SelectOneWithProductsUsingCategoryID</c>). Legs:
/// ADO.NET (baseline, two result sets), Dapper (multi-result), Inquiry (generated eager), DLG.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerLoadingBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;

    [Params(1000)]
    public int Rows;

    // First category id under the benchmark seed. Categories are seeded first (10 rows), so id 1 exists.
    private const int TargetCategoryId = 1;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        var filtered = LoadManyToManyGraphAdoAsync(filteredChildren: true).GetAwaiter().GetResult();
        var oldShape = LoadManyToManyGraphAdoAsync(filteredChildren: false).GetAwaiter().GetResult();
        var inquiryAttached = ManyToMany_InquiryFilteredEager().GetAwaiter().GetResult();
        var expectedFiltered = new ManyToManyGraphCardinality(2, 8, 8, 8);
        var expectedOldShape = new ManyToManyGraphCardinality(2, Rows + 8, 8, 8);
        if (filtered != expectedFiltered || oldShape != expectedOldShape || inquiryAttached != 8)
        {
            throw new InvalidOperationException(
                $"Unexpected M:N evidence cardinality: filtered={filtered}, old-shape={oldShape}, " +
                $"Inquiry attached={inquiryAttached}.");
        }

        WriteManyToManyCardinalityArtifact(filtered, oldShape, inquiryAttached);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

    [BenchmarkCategory("EagerParentChildren"), Benchmark(Baseline = true)]
    public async Task<int> Eager_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;";
        command.Parameters.AddWithValue("@id", TargetCategoryId);
        await using var reader = await command.ExecuteReaderAsync();
        var hasCategory = await reader.ReadAsync();
        await reader.NextResultAsync();
        var childCount = 0;
        while (await reader.ReadAsync()) childCount++;
        return hasCategory ? childCount : -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;",
            new { id = TargetCategoryId });
        _ = await multi.ReadFirstOrDefaultAsync<(int, string)>();
        var children = (await multi.ReadAsync<int>()).AsList();
        return children.Count;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Inquiry()
    {
        var category = await _db.Categories.SelectByKeyWithProductsAsync(TargetCategoryId);
        return category?.Products?.Count ?? -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dlg()
    {
        var category = await DlgLib.Category.SelectOneWithProductsUsingCategoryIDAsync(TargetCategoryId);
        return category?.ProductsUsingCategoryID?.Count ?? -1;
    }

    /// <summary>
    /// Pre-#57 ADO.NET baseline. It transports and materializes every child before the junction
    /// discards unrelated rows; the full graph assembly path is otherwise identical to the filtered leg.
    /// </summary>
    [BenchmarkCategory("ManyToManyChildFiltering"), Benchmark(Baseline = true)]
    public async Task<int> ManyToMany_OldAllChildrenAdo()
    {
        var cardinality = await LoadManyToManyGraphAdoAsync(filteredChildren: false);
        return cardinality.AttachedChildren;
    }

    /// <summary>
    /// Filtered ADO.NET comparison. It uses the same transport, materialization, dictionaries, and
    /// attachment path as the baseline; only the child SELECT is changed to the generated #57 shape.
    /// </summary>
    [BenchmarkCategory("ManyToManyChildFiltering"), Benchmark]
    public async Task<int> ManyToMany_FilteredChildrenAdo()
    {
        var cardinality = await LoadManyToManyGraphAdoAsync(filteredChildren: true);
        return cardinality.AttachedChildren;
    }

    /// <summary>
    /// Real generated Inquiry eager command over the same two parents, eight participating children,
    /// and <see cref="Rows"/> unrelated children.
    /// </summary>
    [BenchmarkCategory("ManyToManyChildFiltering"), Benchmark]
    public async Task<int> ManyToMany_InquiryFilteredEager()
    {
        var attached = 0;
        await foreach (var order in _db.ManyToManyOrders.AllWithProductsAsync())
        {
            attached += order.Products.Count;
        }
        return attached;
    }

    private async Task<ManyToManyGraphCardinality> LoadManyToManyGraphAdoAsync(bool filteredChildren)
    {
        const string parentsSql = "SELECT [Id], [Name] FROM [BenchmarkM2MOrder]";
        const string oldChildrenSql = "SELECT [Id], [Title] FROM [BenchmarkM2MProduct]";
        const string filteredChildrenSql = """
            SELECT [Id], [Title] FROM [BenchmarkM2MProduct]
            WHERE [Id] IN (SELECT [__j].[ProductId] FROM [BenchmarkM2MOrderProduct] [__j]
                WHERE [__j].[OrderId] IN (SELECT [Id] FROM [BenchmarkM2MOrder]))
            """;
        const string junctionsSql = "SELECT [OrderId], [ProductId] FROM [BenchmarkM2MOrderProduct]";

        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = parentsSql + "; "
            + (filteredChildren ? filteredChildrenSql : oldChildrenSql) + "; "
            + junctionsSql + ";";
        await using var reader = await command.ExecuteReaderAsync();

        var parents = new Dictionary<long, BenchmarkM2MOrder>();
        while (await reader.ReadAsync())
        {
            var parent = new BenchmarkM2MOrder
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1)
            };
            parents.Add(parent.Id, parent);
        }

        await reader.NextResultAsync();
        var children = new Dictionary<long, BenchmarkM2MProduct>();
        while (await reader.ReadAsync())
        {
            var child = new BenchmarkM2MProduct
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1)
            };
            children.Add(child.Id, child);
        }

        await reader.NextResultAsync();
        var junctions = new List<BenchmarkM2MOrderProduct>();
        while (await reader.ReadAsync())
        {
            junctions.Add(new BenchmarkM2MOrderProduct
            {
                OrderId = reader.GetInt64(0),
                ProductId = reader.GetInt64(1)
            });
        }

        var attached = 0;
        foreach (var junction in junctions)
        {
            if (parents.TryGetValue(junction.OrderId, out var parent)
                && children.TryGetValue(junction.ProductId, out var child))
            {
                parent.Products.Add(child);
                attached++;
            }
        }

        return new ManyToManyGraphCardinality(parents.Count, children.Count, junctions.Count, attached);
    }

    private void WriteManyToManyCardinalityArtifact(
        ManyToManyGraphCardinality filtered,
        ManyToManyGraphCardinality oldShape,
        int inquiryAttached)
    {
        var path = Path.Combine(
            "BenchmarkDotNet.Artifacts",
            "results",
            "many-to-many-child-filtering-cardinality.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", "inquiry-many-to-many-cardinality-v1");
            writer.WriteNumber("rowsParameter", Rows);
            WriteCardinality(writer, "filteredAdo", filtered);
            WriteCardinality(writer, "oldAllChildrenAdo", oldShape);
            writer.WriteStartObject("inquiryFilteredEager");
            writer.WriteNumber("attachedChildren", inquiryAttached);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        File.WriteAllBytes(path, stream.ToArray());
    }

    private static void WriteCardinality(
        Utf8JsonWriter writer,
        string propertyName,
        ManyToManyGraphCardinality cardinality)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("parentsMaterialized", cardinality.ParentsMaterialized);
        writer.WriteNumber("childrenMaterialized", cardinality.ChildrenMaterialized);
        writer.WriteNumber("junctionsMaterialized", cardinality.JunctionsMaterialized);
        writer.WriteNumber("childrenAttached", cardinality.AttachedChildren);
        writer.WriteEndObject();
    }

    private readonly record struct ManyToManyGraphCardinality(
        int ParentsMaterialized,
        int ChildrenMaterialized,
        int JunctionsMaterialized,
        int AttachedChildren);
}
