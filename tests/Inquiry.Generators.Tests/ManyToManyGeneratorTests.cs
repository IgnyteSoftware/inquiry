using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Many-to-many eager loading ([InquiryManyToMany]): the single-parent load joins the related rows
/// through the junction table (filtered by the junction's parent FK), and the batch (all-eager) load
/// assembles in memory from two queries (all children + all junction rows). Verifies the JOIN SQL, the
/// batch consts, the per-dialect identifier quoting, and the INQ063 validation diagnostics.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string OrderProductSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Orders")]
        public sealed class Order
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;

            [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]
            public List<Product> Products { get; set; } = new();
        }

        [InquiryTable("Products")]
        public sealed class Product
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Title { get; set; } = string.Empty;
        }

        [InquiryTable("OrderProduct")]
        public sealed class OrderProduct
        {
            [InquiryKey] public long OrderId { get; set; }
            [InquiryKey] public long ProductId { get; set; }
        }

        public partial class OrderStore : Inquiry.Stores.InquiryStore<Demo.Order>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Order?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);
        }
        """;

    private static string GetOrderStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("OrderStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void ManyToManyEmitsJoinAndBatchConsts_Sqlite()
    {
        var result = RunGenerator(OrderProductSource);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        // Single-parent JOIN through the junction, filtered by the junction's parent FK.
        Assert.Contains("_sql_Products = \"SELECT \\\"Products\\\".\\\"Id\\\", \\\"Products\\\".\\\"Title\\\" FROM \\\"Products\\\" INNER JOIN \\\"OrderProduct\\\" __j ON __j.\\\"ProductId\\\" = \\\"Products\\\".\\\"Id\\\" WHERE __j.\\\"OrderId\\\" = @Id\";", text);
        // Batch consts: all children + all junction rows (assembled in memory).
        Assert.Contains("_sql_Products_All = \"SELECT \\\"Id\\\", \\\"Title\\\" FROM \\\"Products\\\"\";", text);
        Assert.Contains("_sql_Products_Junction = \"SELECT \\\"OrderId\\\", \\\"ProductId\\\" FROM \\\"OrderProduct\\\"\";", text);
    }

    [Fact]
    public void ManyToManySingleEagerBindsParentKey_Sqlite()
    {
        var result = RunGenerator(OrderProductSource);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        // The single-eager loader fetches parent + products in ONE round trip via the grid reader,
        // binding the parent key (the input key) into the combined multi-result command, and reads the
        // products result set into the navigation.
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Id\", id", text);
        Assert.Contains("_entity.Products = await _grid.ReadListAsync<", text);
    }

    [Fact]
    public void ManyToManyBatchEagerAssemblesInMemory_Sqlite()
    {
        var result = RunGenerator(OrderProductSource);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        // Batch: index children by key, group via the junction rows, no per-parent query.
        Assert.Contains("_childByKey_Products", text);
        Assert.Contains("_grouped_Products", text);
        Assert.Contains("_sql_Products_Junction", text);
    }

    [Fact]
    public void OracleManyToManyJoinUsesUnquotedIdentifiers()
    {
        var result = RunGenerator(OrderProductSource, dialect: "Oracle");
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        Assert.Contains("FROM Products INNER JOIN OrderProduct __j ON __j.ProductId = Products.Id WHERE __j.OrderId = :Id", text);
    }

    [Fact]
    public void ManyToManyOnNonCollectionReportsINQ063()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                // Not a collection — invalid for M:N.
                [InquiryManyToMany(typeof(OrderProduct), "OrderId", "ProductId")]
                public Product Product { get; set; } = new();
            }

            [InquiryTable("Products")]
            public sealed class Product { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("OrderProduct")]
            public sealed class OrderProduct
            {
                [InquiryKey] public long OrderId { get; set; }
                [InquiryKey] public long ProductId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ063");
    }

    [Fact]
    public void ManyToManyWithUnknownJunctionForeignKeyReportsINQ063()
    {
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                // "Nope" is not a property on the junction.
                [InquiryManyToMany(typeof(OrderProduct), "Nope", "ProductId")]
                public List<Product> Products { get; set; } = new();
            }

            [InquiryTable("Products")]
            public sealed class Product { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("OrderProduct")]
            public sealed class OrderProduct
            {
                [InquiryKey] public long OrderId { get; set; }
                [InquiryKey] public long ProductId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ063");
    }
}
