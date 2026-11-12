namespace Inquiry.Generators.Tests;

/// <summary>
/// A property with an Inquiry attribute on a base class is part of the entity: it is a column or a
/// relation, placed before the entity's own properties, base class first.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string InheritedPropertySource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public abstract class Audited
        {
            [InquiryCreatedAt]
            public DateTime CreatedAt { get; set; }

            [InquiryColumn("tenant_id")]
            public int TenantId { get; set; }

            [InquiryColumn("base_code")]
            public virtual string Code { get; set; } = string.Empty;

            [InquiryColumn("base_label")]
            public virtual string Label { get; set; } = string.Empty;

            [InquiryColumn("hidden_tag")]
            public string Tag { get; set; } = string.Empty;

            // Generated code cannot reach a protected property, so it is not mapped.
            [InquiryColumn("secret")]
            protected string Secret { get; set; } = string.Empty;
        }

        public abstract class Keyed<TKey> : Audited
        {
            [InquiryKey]
            public TKey Id { get; set; } = default!;

            [InquiryRelation(nameof(OrderLine.OrderId))]
            public List<OrderLine> Lines { get; set; } = new();
        }

        [InquiryTable("TOrder")]
        public sealed class Order : Keyed<long>
        {
            [InquiryColumn]
            public string Name { get; set; } = string.Empty;

            // An override without an Inquiry attribute keeps the base mapping.
            public override string Code { get; set; } = string.Empty;

            // An override with its own attribute replaces the base mapping.
            [InquiryColumn("order_label")]
            public override string Label { get; set; } = string.Empty;

            // A property that hides the base property replaces it, and this one is not mapped.
            public new int Tag { get; set; }
        }

        [InquiryTable("TOrderLine")]
        public sealed class OrderLine
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryForeignKey(typeof(Order))]
            public long OrderId { get; set; }

            [InquiryForeignKey(typeof(Order), nameof(Order.Code))]
            public string OrderCode { get; set; } = string.Empty;
        }

        public partial class OrderStore : InquiryStore<Order>
        {
            [InquirySelectOneByKey]
            public partial Task<Order?> GetAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectOneByKeyEager]
            public partial Task<Order?> GetWithLinesAsync(long id, CancellationToken cancellationToken = default);

            [InquiryInsert]
            public partial Task<int> InsertAsync(Order order, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void PropertiesDeclaredOnABaseClassAreMappedBaseClassFirst()
    {
        var result = RunGenerator(InheritedPropertySource);
        AssertNoErrors(result);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OrderStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains(
            "\"SELECT \\\"CreatedAt\\\", \\\"tenant_id\\\", \\\"base_code\\\", \\\"Id\\\", \\\"Name\\\", \\\"order_label\\\" FROM \\\"TOrder\\\" WHERE \\\"Id\\\" = @Id\";",
            store);
        Assert.Contains(
            "\"INSERT INTO \\\"TOrder\\\" (\\\"CreatedAt\\\", \\\"tenant_id\\\", \\\"base_code\\\", \\\"Id\\\", \\\"Name\\\", \\\"order_label\\\") VALUES (@CreatedAt, @TenantId, @Code, @Id, @Name, @Label)\";",
            store);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Order.InquiryEntity.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("TenantId = reader.GetInt32(1)", entity);
        Assert.Contains("Id = reader.GetInt64(3)", entity);
        Assert.DoesNotContain("Tag =", entity);
    }

    [Fact]
    public void RelationDeclaredOnABaseClassIsLoaded()
    {
        var result = RunGenerator(InheritedPropertySource);
        AssertNoErrors(result);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OrderStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("private const string _sql_Lines = ", store);
    }

    [Fact]
    public void TypedForeignKeyResolvesAKeyTheTargetInherits()
    {
        var result = RunGenerator(InheritedPropertySource);
        AssertNoErrors(result);

        var schema = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
        // The one-argument typed form needs the single key of the target, which Order inherits.
        Assert.Contains("REFERENCES \"\"TOrder\"\"(\"\"Id\"\")", schema);
        // An explicit target that is an override without an attribute takes the base column name.
        Assert.Contains("REFERENCES \"\"TOrder\"\"(\"\"base_code\"\")", schema);
    }
}
