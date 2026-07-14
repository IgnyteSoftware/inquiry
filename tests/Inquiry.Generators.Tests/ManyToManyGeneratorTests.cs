using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Many-to-many eager loading ([InquiryManyToMany]): the single-parent load joins the related rows
/// through the junction table (filtered by the junction's parent FK), and the batch (all-eager) load
/// assembles in memory from filtered child and junction queries. Verifies the JOIN SQL, the
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
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            [InquiryColumn, InquiryGlobalFilter] public bool IsActive { get; set; } = true;

            [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]
            public List<Product> Products { get; set; } = new();
        }

        [InquiryTable("Products")]
        public sealed class Product
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Title { get; set; } = string.Empty;
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            [InquiryColumn, InquiryGlobalFilter] public bool IsActive { get; set; } = true;
        }

        [InquiryTable("OrderProduct")]
        public sealed class OrderProduct
        {
            [InquiryKey] public long OrderId { get; set; }
            [InquiryKey] public long ProductId { get; set; }
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            [InquiryColumn, InquiryGlobalFilter] public bool IsActive { get; set; } = true;
        }

        public partial class OrderStore : Inquiry.Stores.InquiryStore<Demo.Order>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Order?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);

            [InquirySelectAllEager(IncludeDeleted = true)]
            public partial IAsyncEnumerable<Order> AllIncludingDeletedWithProductsAsync(CancellationToken cancellationToken = default);
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
        Assert.Contains("_sql_Products = \"SELECT \\\"Products\\\".\\\"Id\\\", \\\"Products\\\".\\\"Title\\\", \\\"Products\\\".\\\"IsDeleted\\\", \\\"Products\\\".\\\"IsActive\\\" FROM \\\"Products\\\" INNER JOIN \\\"OrderProduct\\\" \\\"__j\\\" ON \\\"__j\\\".\\\"ProductId\\\" = \\\"Products\\\".\\\"Id\\\" WHERE \\\"__j\\\".\\\"OrderId\\\" = @Id AND \\\"__j\\\".\\\"IsDeleted\\\" = 0 AND \\\"__j\\\".\\\"IsActive\\\" = 1 AND \\\"Products\\\".\\\"IsDeleted\\\" = 0 AND \\\"Products\\\".\\\"IsActive\\\" = 1\";", text);
        Assert.Contains("\\\"Id\\\" IN (SELECT \\\"__j\\\".\\\"ProductId\\\" FROM \\\"OrderProduct\\\" \\\"__j\\\"", text);
        Assert.Contains("\\\"__j\\\".\\\"IsDeleted\\\" = 0 AND \\\"__j\\\".\\\"IsActive\\\" = 1 AND \\\"__j\\\".\\\"OrderId\\\" IN (SELECT \\\"Id\\\" FROM \\\"Orders\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1)", text);
        Assert.Contains("_sql_Products_Junction = \"SELECT \\\"OrderId\\\", \\\"ProductId\\\", \\\"IsDeleted\\\", \\\"IsActive\\\" FROM \\\"OrderProduct\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1 AND \\\"OrderId\\\" IN (SELECT \\\"Id\\\" FROM \\\"Orders\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1) AND \\\"ProductId\\\" IN (SELECT \\\"Id\\\" FROM \\\"Products\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1)\";", text);
        Assert.Contains("_sql_Products_All_IncludeDeleted", text);
        Assert.Contains("IN (SELECT \\\"Id\\\" FROM \\\"Orders\\\" WHERE \\\"IsActive\\\" = 1)", text);
        Assert.Contains("_sql_Products_Junction_IncludeDeleted", text);
        Assert.Equal(2, text.Split(new[] { "_sql_Products_All_IncludeDeleted" }, StringSplitOptions.None).Length - 1);
        Assert.Equal(2, text.Split(new[] { "_sql_Products_Junction_IncludeDeleted" }, StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("Sqlite", "\"", "\"", "\"__j\"", "0", "1")]
    [InlineData("SqlServer", "[", "]", "[__j]", "0", "1")]
    [InlineData("PostgreSql", "\"", "\"", "\"__j\"", "FALSE", "TRUE")]
    [InlineData("MySql", "`", "`", "`__j`", "0", "1")]
    [InlineData("MariaDb", "`", "`", "`__j`", "0", "1")]
    [InlineData("Oracle", "", "", "\"__j\"", "0", "1")]
    public void ManyToManyBatchChildSqlFiltersThroughJunctionAndParent_AllDialects(
        string dialect,
        string quoteOpen,
        string quoteClose,
        string junctionAlias,
        string falseLiteral,
        string trueLiteral)
    {
        var result = RunGenerator(OrderProductSource, dialect: dialect);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);
        string Q(string name) => quoteOpen + name + quoteClose;

        var expected = "SELECT " + Q("Id") + ", " + Q("Title") + ", " + Q("IsDeleted") + ", " + Q("IsActive")
            + " FROM " + Q("Products")
            + " WHERE " + Q("IsDeleted") + " = " + falseLiteral + " AND " + Q("IsActive") + " = " + trueLiteral
            + " AND " + Q("Id") + " IN (SELECT " + junctionAlias + "." + Q("ProductId")
            + " FROM " + Q("OrderProduct") + " " + junctionAlias
            + " WHERE " + junctionAlias + "." + Q("IsDeleted") + " = " + falseLiteral
            + " AND " + junctionAlias + "." + Q("IsActive") + " = " + trueLiteral
            + " AND " + junctionAlias + "." + Q("OrderId") + " IN (SELECT " + Q("Id") + " FROM " + Q("Orders")
            + " WHERE " + Q("IsDeleted") + " = " + falseLiteral + " AND " + Q("IsActive") + " = " + trueLiteral + "))";

        Assert.Contains("_sql_Products_All = \"" + EscapeGeneratedString(expected) + "\";", text);
        Assert.DoesNotContain("@", expected);
    }

    [Theory]
    [InlineData("Sqlite", "\"", "\"", "\"__j\"", "0", "1")]
    [InlineData("SqlServer", "[", "]", "[__j]", "0", "1")]
    [InlineData("PostgreSql", "\"", "\"", "\"__j\"", "FALSE", "TRUE")]
    [InlineData("MySql", "`", "`", "`__j`", "0", "1")]
    [InlineData("MariaDb", "`", "`", "`__j`", "0", "1")]
    [InlineData("Oracle", "", "", "\"__j\"", "0", "1")]
    public void ManyToManyBatchChildSqlSupportsSchemasWithoutQualifiedColumnReferences(
        string dialect,
        string quoteOpen,
        string quoteClose,
        string junctionAlias,
        string falseLiteral,
        string trueLiteral)
    {
        var source = OrderProductSource
            .Replace("[InquiryTable(\"Orders\")]", "[InquiryTable(\"Orders\", Schema = \"app\")]")
            .Replace("[InquiryTable(\"Products\")]", "[InquiryTable(\"Products\", Schema = \"app\")]")
            .Replace("[InquiryTable(\"OrderProduct\")]", "[InquiryTable(\"OrderProduct\", Schema = \"app\")]");
        var result = RunGenerator(source, dialect: dialect);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);
        string Q(string name) => quoteOpen + name + quoteClose;
        string T(string name) => Q("app") + "." + Q(name);

        var expected = "SELECT " + Q("Id") + ", " + Q("Title") + ", " + Q("IsDeleted") + ", " + Q("IsActive")
            + " FROM " + T("Products")
            + " WHERE " + Q("IsDeleted") + " = " + falseLiteral + " AND " + Q("IsActive") + " = " + trueLiteral
            + " AND " + Q("Id") + " IN (SELECT " + junctionAlias + "." + Q("ProductId")
            + " FROM " + T("OrderProduct") + " " + junctionAlias
            + " WHERE " + junctionAlias + "." + Q("IsDeleted") + " = " + falseLiteral
            + " AND " + junctionAlias + "." + Q("IsActive") + " = " + trueLiteral
            + " AND " + junctionAlias + "." + Q("OrderId") + " IN (SELECT " + Q("Id") + " FROM " + T("Orders")
            + " WHERE " + Q("IsDeleted") + " = " + falseLiteral + " AND " + Q("IsActive") + " = " + trueLiteral + "))";

        Assert.Contains("_sql_Products_All = \"" + EscapeGeneratedString(expected) + "\";", text);
        Assert.DoesNotContain(T("Products") + "." + Q("Id") + " IN", expected);
    }

    private static string EscapeGeneratedString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [Fact]
    public void ManyToManySingleEagerBindsParentKey_Sqlite()
    {
        var result = RunGenerator(OrderProductSource);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        // The single-eager loader fetches parent + products in ONE round trip via the grid reader,
        // binding the parent key (the input key) into the combined multi-result command, and reads the
        // products result set into the navigation.
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<long>(", text);
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_p0.Value = (object?)_key ?? global::System.DBNull.Value;", text);
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
    public void OracleManyToManyJoinQuotesJunctionAlias()
    {
        var result = RunGenerator(OrderProductSource, dialect: "Oracle");
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        Assert.Contains("FROM Products INNER JOIN OrderProduct \\\"__j\\\" ON \\\"__j\\\".ProductId = Products.Id WHERE \\\"__j\\\".OrderId = :iq1$Idxxxx$30d4cf864d6e68", text);
    }

    [Fact]
    public void OracleEagerUsesReturnResultPlSqlGrid()
    {
        // Oracle cannot return multiple result sets from a ;-separated command (ORA-00933), so the grid
        // command is wrapped in a DBMS_SQL.RETURN_RESULT PL/SQL block (implicit result sets) instead —
        // single eager (parent + JOIN) and batch eager (parent + children + junction) alike.
        var result = RunGenerator(OrderProductSource, dialect: "Oracle");
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sqlSelectByKey + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Products + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
        Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sqlSelectAll + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Products_All + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Products_Junction + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
        Assert.Contains("_entity.Products = await _grid.ReadListAsync<", text);
        // No per-relation streaming query on the grid path.
        Assert.DoesNotContain("await foreach (var _child in Inquiry.QueryAsync<", text);
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
