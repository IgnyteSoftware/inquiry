using Inquiry.Generators.Abstractions;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Relationship key resolution and junction correctness: one exact property-name rule for foreign keys
/// (#258, #278), stored-representation checks (#276), structural byte-array keys (#277), declared junction
/// targets (#275), ineffective auto-junction options (#279), and auto-junction column routing and facets
/// (#280, #281).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    // Every name differs: the parent key property (RegionId) and column (region_key), and the child
    // foreign-key property (RegionRef) and column (region_id). A rule that mixed property and column names
    // anywhere would bind a parameter the SQL does not declare.
    private const string RelationKeyNameSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Region")]
        public sealed class Region
        {
            [InquiryKey("region_key")] public int RegionId { get; set; }

            [InquiryRelation(COLLECTION_FK)]
            public List<Territory> Territories { get; set; } = new();
        }

        [InquiryTable("Territory")]
        public sealed class Territory
        {
            [InquiryKey] public int TerritoryId { get; set; }
            [InquiryColumn("region_id")] public int RegionRef { get; set; }

            [InquiryRelation(REFERENCE_FK)]
            public Region? Region { get; set; }
        }

        public partial class RegionStore : InquiryStore<Region>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Region?> GetWithTerritoriesAsync(int regionId, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Region> AllWithTerritoriesAsync(CancellationToken cancellationToken = default);
        }

        public partial class TerritoryStore : InquiryStore<Territory>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Territory?> GetWithRegionAsync(int territoryId, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Territory> AllWithRegionAsync(CancellationToken cancellationToken = default);
        }
        """;

    private static string RelationKeyNames(string collectionForeignKey, string referenceForeignKey)
        => RelationKeyNameSource
            .Replace("COLLECTION_FK", collectionForeignKey)
            .Replace("REFERENCE_FK", referenceForeignKey);

    private static string StoreText(GeneratorTestResult result, string storeName)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            t => t.FilePath.EndsWith(storeName + ".InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    private static Diagnostic[] CompileErrors(GeneratorTestResult result)
        => result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelationForeignKeyWithADifferentColumnNameBindsTheParameterTheSqlDeclares(bool separateQueries)
    {
        var result = RunGenerator(
            RelationKeyNames("nameof(Territory.RegionRef)", "nameof(RegionRef)"),
            dialect: separateQueries ? "NoGrid" : "Sqlite",
            includeNoGridGenerator: separateQueries);

        // The test-only NoGrid dialect is not a shipped provider, so it always reports INQ043.
        Assert.Empty(result.RunResult.Diagnostics.Where(static d => d.Id != "INQ043"));
        Assert.Empty(CompileErrors(result));

        // The collection SQL filters the child's COLUMN and names the parameter after its PROPERTY; the
        // binder must use that same property name, on both the grid and the separate-query path.
        var regions = StoreText(result, "RegionStore");
        Assert.Contains("_sql_Territories = \"SELECT \\\"TerritoryId\\\", \\\"region_id\\\" FROM \\\"Territory\\\" WHERE \\\"region_id\\\" = @RegionRef\";", regions);
        Assert.Contains("ParameterName = \"@RegionRef\"", regions);
        Assert.DoesNotContain("@region_id", regions);
        Assert.Contains(separateQueries ? "TaskAsyncEnumerableExtensions.ConfigureAwait(Inquiry.QueryAsync<" : "Inquiry.QueryMultipleAsync(", regions);

        // The reference side reads the foreign key through its property and joins on its column.
        var territories = StoreText(result, "TerritoryStore");
        Assert.Contains("_entity.RegionRef", territories);
        Assert.Contains("\\\"region_key\\\" IN (SELECT \\\"region_id\\\" FROM \\\"Territory\\\")", territories);
    }

    [Theory]
    [InlineData("\"region_id\"", false)]
    [InlineData("\"regionRef\"", false)]
    [InlineData("\"region_id\"", true)]
    [InlineData("\"regionRef\"", true)]
    public void RelationForeignKeyNamedByColumnOrOtherCasingReportsINQ040AndEmitsNoLoader(string name, bool suppressed)
    {
        // Validation always required the exact property name, but emission matched column names and other
        // casings too. With INQ040 suppressed, the store was then emitted binding @region_id against SQL that
        // declares @RegionRef, and reading _entity.region_id, which does not compile.
        var result = RunGenerator(
            RelationKeyNames(name, name),
            additionalDiagnosticOptions: suppressed
                ? new Dictionary<string, ReportDiagnostic> { ["INQ040"] = ReportDiagnostic.Suppress }
                : null);

        var diagnostics = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ040").ToArray();
        Assert.Equal(suppressed ? 0 : 2, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Contains("use 'RegionRef'", d.GetMessage(), StringComparison.Ordinal));

        // No eager loader is emitted, so any follow-on error is a missing partial body.
        Assert.DoesNotContain("_sql_Territories", StoreText(result, "RegionStore"));
        Assert.DoesNotContain("_sql_Region", StoreText(result, "TerritoryStore"));
        Assert.All(CompileErrors(result), static d => Assert.Equal("CS8795", d.Id));
    }

    // The junction maps property PostId to column "TagId" and property TagId to column "PostId". A name
    // resolved by column, or by whichever column happens to come first, joins the wrong way round.
    private const string CollidingJunctionSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Posts")]
        public sealed class Post
        {
            [InquiryKey] public long Id { get; set; }

            [InquiryManyToMany(typeof(PostTag), PARENT_FK, CHILD_FK)]
            public List<Tag> Tags { get; set; } = new();
        }

        [InquiryTable("Tags")]
        public sealed class Tag
        {
            [InquiryKey] public long Id { get; set; }
        }

        [InquiryTable("PostTag")]
        public sealed class PostTag
        {
            JUNCTION_COLUMNS
        }

        public partial class PostStore : InquiryStore<Post>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Post?> GetWithTagsAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Post> AllWithTagsAsync(CancellationToken cancellationToken = default);
        }
        """;

    private const string PostIdToTagIdColumn = "[InquiryKey(\"TagId\")] public long PostId { get; set; }";
    private const string TagIdToPostIdColumn = "[InquiryKey(\"PostId\")] public long TagId { get; set; }";

    private static string CollidingJunction(string parent, string child, bool tagIdFirst)
        => CollidingJunctionSource
            .Replace("PARENT_FK", parent)
            .Replace("CHILD_FK", child)
            .Replace(
                "JUNCTION_COLUMNS",
                tagIdFirst ? TagIdToPostIdColumn + "\n    " + PostIdToTagIdColumn : PostIdToTagIdColumn + "\n    " + TagIdToPostIdColumn);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JunctionForeignKeysResolveByPropertyWhenColumnNamesCollideInEitherDeclarationOrder(bool tagIdFirst)
    {
        var result = RunGenerator(CollidingJunction("nameof(PostTag.PostId)", "nameof(PostTag.TagId)", tagIdFirst));

        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(CompileErrors(result));

        // Property PostId is column "TagId", and property TagId is column "PostId". Before the shared rule,
        // declaring TagId first resolved "PostId" to it by column name and transposed the join.
        var text = StoreText(result, "PostStore");
        Assert.Contains(
            "INNER JOIN \\\"PostTag\\\" \\\"__j\\\" ON \\\"__j\\\".\\\"PostId\\\" = \\\"Tags\\\".\\\"Id\\\" WHERE \\\"__j\\\".\\\"TagId\\\" = @Id",
            text);
        Assert.Contains("_sql_Tags_Junction = \"SELECT \\\"TagId\\\", \\\"PostId\\\" FROM \\\"PostTag\\\"", text);
    }

    [Theory]
    [InlineData("\"postId\"", "nameof(PostTag.TagId)", "postId", "PostId", false, false)]
    [InlineData("nameof(PostTag.PostId)", "\"tagid\"", "tagid", "TagId", false, false)]
    [InlineData("\"postId\"", "nameof(PostTag.TagId)", "postId", "PostId", true, false)]
    [InlineData("nameof(PostTag.PostId)", "\"tagid\"", "tagid", "TagId", true, false)]
    [InlineData("\"postId\"", "nameof(PostTag.TagId)", "postId", "PostId", false, true)]
    [InlineData("nameof(PostTag.PostId)", "\"tagid\"", "tagid", "TagId", false, true)]
    public void JunctionForeignKeyNamedByOtherCasingReportsINQ088AndEmitsNoLoader(
        string parent, string child, string named, string property, bool suppressed, bool tagIdFirst)
    {
        var result = RunGenerator(
            CollidingJunction(parent, child, tagIdFirst),
            additionalDiagnosticOptions: suppressed
                ? new Dictionary<string, ReportDiagnostic> { ["INQ088"] = ReportDiagnostic.Suppress }
                : null);

        if (!suppressed)
        {
            var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ088");
            Assert.Contains($"names '{named}'", diagnostic.GetMessage(), StringComparison.Ordinal);
            Assert.Contains($"use '{property}'", diagnostic.GetMessage(), StringComparison.Ordinal);
        }

        // Validation and emission now agree, so a rejected name never reaches emitted SQL, even with the
        // diagnostic suppressed.
        Assert.DoesNotContain("_sql_Tags", StoreText(result, "PostStore"));
        Assert.All(CompileErrors(result), static d => Assert.Equal("CS8795", d.Id));
    }

    [Fact]
    public void ExplicitManyToManyReportsEachAutoJunctionOptionAtItsArgument()
    {
        var source = OrderProductSource.Replace(
            "[InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]",
            "[InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId), "
            + "JunctionTable = \"legacy_links\", JunctionSchema = \"legacy\", ParentColumn = \"order_ref\", ChildColumn = \"product_ref\")]");

        var result = RunGenerator(source);

        // Entity-discovery diagnostics come from every provider generator this harness loads, so keep one
        // per location.
        var diagnostics = result.RunResult.Diagnostics
            .Where(static d => d.Id == "INQ103")
            .GroupBy(static d => d.Location.SourceSpan)
            .Select(static g => g.First())
            .ToArray();
        Assert.All(diagnostics, static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Equal(
            new[] { "\"legacy\"", "\"legacy_links\"", "\"order_ref\"", "\"product_ref\"" },
            diagnostics.Select(d => source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)).OrderBy(static s => s, StringComparer.Ordinal));
        Assert.Contains(diagnostics, static d => d.GetMessage().Contains("sets JunctionTable", StringComparison.Ordinal));

        // The options stay ignored: the explicit junction's own table is still used.
        Assert.Empty(CompileErrors(result));
        Assert.Contains("INNER JOIN \\\"OrderProduct\\\" \\\"__j\\\"", GetOrderStore(result));
        Assert.DoesNotContain("legacy_links", GetOrderStore(result));
    }

    [Fact]
    public void ValidExplicitAndAutoManagedManyToManyReportNoINQ103()
    {
        Assert.DoesNotContain(RunGenerator(OrderProductSource).RunResult.Diagnostics, static d => d.Id == "INQ103");
        Assert.DoesNotContain(
            RunGenerator(AutoJunctionSource.Replace(
                "[InquiryManyToMany]",
                "[InquiryManyToMany(JunctionTable = \"order_product\", ParentColumn = \"order_id\", ChildColumn = \"product_id\")]"))
                .RunResult.Diagnostics,
            static d => d.Id == "INQ103");
    }

    // The related key is a numeric enum. The junction stores the same enum as text, so the SQL comparison
    // can never match even though both properties have the same CLR type.
    private const string EnumStorageSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public enum Grade { Low = 1, High = 2 }

        [InquiryTable("Posts")]
        public sealed class Post
        {
            [InquiryKey] public long Id { get; set; }

            [InquiryManyToMany(typeof(PostGrade), nameof(PostGrade.PostId), nameof(PostGrade.Grade))]
            public List<GradeRow> Grades { get; set; } = new();
        }

        [InquiryTable("Grades")]
        public sealed class GradeRow
        {
            [InquiryKey] public Grade Code { get; set; }
        }

        [InquiryTable("PostGrade")]
        public sealed class PostGrade
        {
            [InquiryKey] public long PostId { get; set; }
            [InquiryKey(Length = 16), JUNCTION_STORAGE] public Grade Grade { get; set; }
        }

        public partial class PostStore : InquiryStore<Post>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Post> AllWithGradesAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ManyToManyJoiningANumericEnumKeyToATextEnumForeignKeyReportsINQ104()
    {
        var result = RunGenerator(EnumStorageSource.Replace("JUNCTION_STORAGE", "InquiryEnumAsString"));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(
            "joins 'PostGrade.Grade' (enum stored as text) to 'GradeRow.Code' (enum stored as a number)",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);

        // Before, this compiled with no diagnostic and returned no related rows. The loader is now dropped
        // even when the error is suppressed.
        Assert.DoesNotContain("_sql_Grades", StoreText(result, "PostStore"));
        var suppressed = RunGenerator(
            EnumStorageSource.Replace("JUNCTION_STORAGE", "InquiryEnumAsString"),
            additionalDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["INQ104"] = ReportDiagnostic.Suppress });
        Assert.DoesNotContain("_sql_Grades", StoreText(suppressed, "PostStore"));
    }

    [Fact]
    public void ManyToManyWithMatchingEnumStorageReportsNoINQ104()
    {
        var result = RunGenerator(EnumStorageSource.Replace(", JUNCTION_STORAGE", string.Empty));

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Empty(CompileErrors(result));
        Assert.Contains("_sql_Grades_All", StoreText(result, "PostStore"));
    }

    // A composite key whose second component is converter-backed. Two different converters can store one
    // model value two ways, so joining across them is rejected rather than guessed.
    private const string CompositeConverterSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public readonly record struct Sku(int Value);

        public sealed class SkuAsText : IInquiryValueConverter<Sku, string>
        {
            public string ToProvider(Sku model) => model.Value.ToString();
            public Sku FromProvider(string provider) => new(int.Parse(provider));
        }

        public sealed class SkuAsPaddedText : IInquiryValueConverter<Sku, string>
        {
            public string ToProvider(Sku model) => model.Value.ToString("D8");
            public Sku FromProvider(string provider) => new(int.Parse(provider));
        }

        [InquiryTable("Orders")]
        public sealed class Order
        {
            [InquiryKey] public long Id { get; set; }

            [InquiryManyToMany(typeof(OrderItem), nameof(OrderItem.OrderId), nameof(OrderItem.TenantId), nameof(OrderItem.Sku))]
            public List<Item> Items { get; set; } = new();
        }

        [InquiryTable("Items")]
        public sealed class Item
        {
            [InquiryKey] public int TenantId { get; set; }
            [InquiryKey(Length = 16, Converter = typeof(SkuAsText))] public Sku Sku { get; set; }
        }

        [InquiryTable("OrderItem")]
        public sealed class OrderItem
        {
            [InquiryKey] public long OrderId { get; set; }
            [InquiryKey] public int TenantId { get; set; }
            [InquiryKey(Length = 16, Converter = typeof(JUNCTION_CONVERTER))] public Sku Sku { get; set; }
        }

        public partial class OrderStore : InquiryStore<Order>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Order> AllWithItemsAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void CompositeManyToManyJoiningDifferentConvertersReportsINQ104()
    {
        var result = RunGenerator(CompositeConverterSource.Replace("JUNCTION_CONVERTER", "SkuAsPaddedText"));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Contains(
            "joins 'OrderItem.Sku' (converter Demo.SkuAsPaddedText) to 'Item.Sku' (converter Demo.SkuAsText)",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
        Assert.DoesNotContain("_sql_Items", GetOrderStore(result));

        var matching = RunGenerator(CompositeConverterSource.Replace("JUNCTION_CONVERTER", "SkuAsText"));
        Assert.DoesNotContain(matching.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Empty(CompileErrors(matching));
    }

    [Fact]
    public void TypedIdKeyReferencedByAPlainForeignKeyOfTheSameStoredKindReportsNoINQ104()
    {
        // A converter on one side only is fine when both columns store the same kind of value. Typed-ID
        // schemas convert the key to int and keep the foreign key a plain int.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public readonly record struct CustomerId(int Value);

            public sealed class CustomerIdConverter : IInquiryValueConverter<CustomerId, int>
            {
                public int ToProvider(CustomerId model) => model.Value;
                public CustomerId FromProvider(int provider) => new(provider);
            }

            [InquiryTable("Customers")]
            public sealed class Customer
            {
                [InquiryKey(Converter = typeof(CustomerIdConverter))] public CustomerId Id { get; set; }

                [InquiryRelation(nameof(Order.CustomerId))]
                public List<Order> Orders { get; set; } = new();
            }

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn] public long CustomerId { get; set; }
            }

            public partial class CustomerStore : InquiryStore<Customer>
            {
                [InquirySelectOneByKeyEager]
                public partial Task<Customer?> GetWithOrdersAsync(CustomerId id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Contains("_sql_Orders", StoreText(result, "CustomerStore"));
    }

    [Fact]
    public void ConverterOnOneSideOnlyIsNotJudged()
    {
        // On SQLite a native Guid key and a Guid foreign key converted to text store the same text, so a
        // one-sided converter cannot be judged without knowing the provider. It is accepted.
        var source = EnumStorageSource
            .Replace("public enum Grade { Low = 1, High = 2 }", """
                public sealed class GuidAsText : IInquiryValueConverter<global::System.Guid, string>
                {
                    public string ToProvider(global::System.Guid model) => model.ToString("D").ToUpperInvariant();
                    public global::System.Guid FromProvider(string provider) => global::System.Guid.Parse(provider);
                }
                """)
            .Replace("[InquiryKey] public Grade Code { get; set; }", "[InquiryKey] public global::System.Guid Code { get; set; }")
            .Replace(
                "[InquiryKey(Length = 16), JUNCTION_STORAGE] public Grade Grade { get; set; }",
                "[InquiryKey(Length = 36, Converter = typeof(GuidAsText))] public global::System.Guid Grade { get; set; }");

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Empty(CompileErrors(result));
        Assert.Contains("_sql_Grades_All", StoreText(result, "PostStore"));
    }

    [Fact]
    public void SingleKeyManyToManyWithAnIntForeignKeyToADecimalKeyReportsNoINQ104()
    {
        // Numbers of different types compare in SQL, and the generated lookup converts int to decimal.
        var source = EnumStorageSource
            .Replace("[InquiryKey] public Grade Code { get; set; }", "[InquiryKey(Precision = 9, Scale = 2)] public decimal Code { get; set; }")
            .Replace("[InquiryKey(Length = 16), JUNCTION_STORAGE] public Grade Grade { get; set; }", "[InquiryKey] public int Grade { get; set; }");

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Empty(CompileErrors(result));
        Assert.Contains("_sql_Grades_All", StoreText(result, "PostStore"));
    }

    [Fact]
    public void CollectionRelationJoiningDifferentEnumStorageReportsINQ104()
    {
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            public enum Region { North = 1, South = 2 }

            [InquiryTable("Regions")]
            public sealed class RegionRow
            {
                [InquiryKey] public Region Code { get; set; }

                [InquiryRelation(nameof(Office.Region))]
                public List<Office> Offices { get; set; } = new();
            }

            [InquiryTable("Offices")]
            public sealed class Office
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Length = 16), InquiryEnumAsString] public Region Region { get; set; }
            }
            """;

        var diagnostic = Assert.Single(RunGenerator(source).RunResult.Diagnostics, static d => d.Id == "INQ104");
        Assert.Contains(
            "joins 'Office.Region' (enum stored as text) to 'RegionRow.Code' (enum stored as a number)",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    // The issue's same-typed composite key: both components are int, so INQ089's type check cannot see a
    // transposition. The junction's declared foreign-key targets can.
    private const string DeclaredTargetSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Posts")]
        public sealed class Post
        {
            [InquiryKey] public long Id { get; set; }

            [InquiryManyToMany(typeof(PostTag), nameof(PostTag.PostId), CHILD_FKS)]
            public List<Tag> Tags { get; set; } = new();
        }

        [InquiryTable("Tags")]
        public sealed class Tag
        {
            [InquiryKey] public int TenantId { get; set; }
            [InquiryKey] public int CategoryId { get; set; }
        }

        [InquiryTable("PostTag")]
        public sealed class PostTag
        {
            [InquiryKey] PARENT_TARGET public long PostId { get; set; }
            [InquiryKey] TENANT_TARGET public int TenantId { get; set; }
            [InquiryKey] CATEGORY_TARGET public int CategoryId { get; set; }
        }

        public partial class PostStore : InquiryStore<Post>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Post> AllWithTagsAsync(CancellationToken cancellationToken = default);
        }
        """;

    private const string InDeclarationOrder = "nameof(PostTag.TenantId), nameof(PostTag.CategoryId)";
    private const string Transposed = "nameof(PostTag.CategoryId), nameof(PostTag.TenantId)";

    private static string DeclaredTargets(string childForeignKeys, bool withTargets, string parentTarget = "")
        => DeclaredTargetSource
            .Replace("CHILD_FKS", childForeignKeys)
            .Replace("PARENT_TARGET", parentTarget)
            .Replace("TENANT_TARGET", withTargets ? "[InquiryForeignKey(typeof(Tag), nameof(Tag.TenantId))]" : string.Empty)
            .Replace("CATEGORY_TARGET", withTargets ? "[InquiryForeignKey(\"Tags\", \"CategoryId\")]" : string.Empty);

    [Fact]
    public void TransposedJunctionKeysWithDeclaredTargetsReportINQ105()
    {
        var result = RunGenerator(DeclaredTargets(Transposed, withTargets: true));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ105");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(
            "'CategoryId' declares a foreign key to 'Tags.CategoryId', but the relation pairs it with 'Tags.TenantId'",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ089");
        Assert.DoesNotContain("_sql_Tags", StoreText(result, "PostStore"));
    }

    [Fact]
    public void JunctionParentKeyDeclaredAgainstTheRelatedTableReportsINQ105()
    {
        var result = RunGenerator(DeclaredTargets(
            InDeclarationOrder, withTargets: false, parentTarget: "[InquiryForeignKey(\"Tags\", \"TenantId\")]"));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ105");
        Assert.Contains(
            "'PostId' declares a foreign key to 'Tags.TenantId', but the relation pairs it with 'Posts.Id'",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void JunctionKeysInDeclarationOrderWithDeclaredTargetsAreAccepted()
    {
        var result = RunGenerator(DeclaredTargets(InDeclarationOrder, withTargets: true, parentTarget: "[InquiryForeignKey(typeof(Post))]"));

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ089" or "INQ105");
        Assert.Empty(CompileErrors(result));
    }

    [Fact]
    public void JunctionKeysWithoutDeclaredTargetsKeepPositionalPairing()
    {
        // No metadata, so nothing contradicts the documented contract: the transposed list is accepted and
        // pairs exactly as written. Names are deliberately not compared.
        var result = RunGenerator(DeclaredTargets(Transposed, withTargets: false));

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ089" or "INQ105");
        Assert.Empty(CompileErrors(result));
        Assert.Contains(
            "\\\"__j\\\".\\\"CategoryId\\\" = \\\"Tags\\\".\\\"TenantId\\\" AND \\\"__j\\\".\\\"TenantId\\\" = \\\"Tags\\\".\\\"CategoryId\\\"",
            StoreText(result, "PostStore"));
    }

    // byte[] keys on both an ordinary collection relation and a many-to-many association.
    private const string ByteArrayKeySource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Docs")]
        public sealed class Doc
        {
            [InquiryKey(Length = 16)] public byte[] Hash { get; set; } = new byte[0];

            [InquiryRelation(nameof(Note.DocHash))]
            public List<Note> Notes { get; set; } = new();

            [InquiryManyToMany(typeof(DocTag), nameof(DocTag.DocHash), nameof(DocTag.TagHash))]
            public List<Tag> Tags { get; set; } = new();
        }

        [InquiryTable("Notes")]
        public sealed class Note
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(Length = 16)] public byte[]? DocHash { get; set; }

            [InquiryRelation(nameof(DocHash))]
            public Doc? Doc { get; set; }
        }

        [InquiryTable("Tags")]
        public sealed class Tag
        {
            [InquiryKey(Length = 16)] public byte[] Hash { get; set; } = new byte[0];
        }

        [InquiryTable("DocTag")]
        public sealed class DocTag
        {
            [InquiryKey(Length = 16)] public byte[] DocHash { get; set; } = new byte[0];
            [InquiryKey(Length = 16)] public byte[] TagHash { get; set; } = new byte[0];
        }

        public partial class DocStore : InquiryStore<Doc>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Doc> AllWithNotesAndTagsAsync(CancellationToken cancellationToken = default);
        }

        public partial class NoteStore : InquiryStore<Note>
        {
            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Note> AllWithDocAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ByteArrayRelationKeysIndexEagerDictionariesByValue()
    {
        var result = RunGenerator(ByteArrayKeySource);

        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(CompileErrors(result));

        // Arrays compare by reference, so a dictionary keyed by byte[] never matched a key read from
        // another row. Every eager dictionary, and every lookup into one, now uses the Base64 text.
        var docs = StoreText(result, "DocStore");
        Assert.DoesNotContain("Dictionary<byte[]", docs);
        Assert.Contains("var _grouped_Notes = new global::System.Collections.Generic.Dictionary<string, ", docs);
        Assert.Contains("var _fkVal = global::System.Convert.ToBase64String(_c.DocHash!);", docs);
        Assert.Contains("var _childByKey_Tags = new global::System.Collections.Generic.Dictionary<string, global::Demo.Tag>", docs);
        Assert.Contains("_childByKey_Tags[global::System.Convert.ToBase64String(_c.Hash)] = _c;", docs);
        Assert.Contains("_childByKey_Tags.TryGetValue(global::System.Convert.ToBase64String(_jChildKey), out var _child)", docs);
        Assert.Contains("_grouped_Tags.TryGetValue(global::System.Convert.ToBase64String(_jParentKey), out var _grp)", docs);
        Assert.Contains("_grouped_Notes.TryGetValue(global::System.Convert.ToBase64String(_entity.Hash), out var _rel_Notes)", docs);

        var notes = StoreText(result, "NoteStore");
        Assert.DoesNotContain("Dictionary<byte[]", notes);
        Assert.Contains("_parents_Doc[global::System.Convert.ToBase64String(_p.Hash)] = _p;", notes);
        Assert.Contains("_parents_Doc.TryGetValue(global::System.Convert.ToBase64String(_entity.DocHash!), out var _rel_Doc)", notes);
    }

    [Fact]
    public void ByteArrayForeignKeyToATypedBinaryKeyKeepsItsImplicitConversion()
    {
        // The dictionary is keyed by the typed key, so the byte[] foreign key must reach TryGetValue as is
        // and convert implicitly. Normalizing it to Base64 text produced CS1503.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public readonly record struct BinaryId(byte[] Value)
            {
                public static implicit operator BinaryId(byte[] value) => new(value);
            }

            public sealed class BinaryIdConverter : IInquiryValueConverter<BinaryId, byte[]>
            {
                public byte[] ToProvider(BinaryId model) => model.Value;
                public BinaryId FromProvider(byte[] provider) => new(provider);
            }

            [InquiryTable("Docs")]
            public sealed class Doc
            {
                [InquiryKey(Length = 16, Converter = typeof(BinaryIdConverter))] public BinaryId Id { get; set; }
            }

            [InquiryTable("Notes")]
            public sealed class Note
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Length = 16)] public byte[] DocId { get; set; } = new byte[0];

                [InquiryRelation(nameof(DocId))]
                public Doc? Doc { get; set; }
            }

            public partial class NoteStore : InquiryStore<Note>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Note> AllWithDocAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(CompileErrors(result));
        Assert.Contains("_parents_Doc.TryGetValue(_entity.DocId, out var _rel_Doc)", StoreText(result, "NoteStore"));
    }

    [Fact]
    public void AutoJunctionColumnOverridesRouteEachNavigationThroughItsOwnColumns()
    {
        // Asymmetric overrides from both sides. Each navigation must filter on the column that references
        // its OWN entity and join the related table through the other column; a transposition would swap
        // the two in either the single-parent JOIN or the batch reads.
        var source = AutoJunctionSource
            .Replace(
                "[InquiryManyToMany]",
                """[InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "order_ref", ChildColumn = "product_ref")]""")
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "product_ref", ChildColumn = "order_ref")]
                    public List<Order> Orders { get; set; } = new();
                """)
            .Replace(
                """
                    public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);
                }
                """,
                """
                    public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);
                }

                public partial class ProductStore : Inquiry.Stores.InquiryStore<Demo.Product>
                {
                    [InquirySelectOneByKeyEager]
                    public partial Task<Product?> GetWithOrdersAsync(long id, CancellationToken cancellationToken = default);

                    [InquirySelectAllEager]
                    public partial IAsyncEnumerable<Product> AllWithOrdersAsync(CancellationToken cancellationToken = default);
                }
                """);

        var result = RunGenerator(source);

        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(CompileErrors(result));

        // DDL and keys stay as before (existing override assertions).
        var schema = GetSchema(result);
        Assert.Contains("PRIMARY KEY (\"\"order_ref\"\", \"\"product_ref\"\")", schema);

        var orders = GetOrderStore(result);
        Assert.Contains(
            "INNER JOIN \\\"order_product\\\" \\\"__j\\\" ON \\\"__j\\\".\\\"product_ref\\\" = \\\"Products\\\".\\\"Id\\\" WHERE \\\"__j\\\".\\\"order_ref\\\" = @Id",
            orders);
        Assert.Contains("_sql_Products_Junction = \"SELECT \\\"order_ref\\\", \\\"product_ref\\\" FROM \\\"order_product\\\"", orders);
        Assert.Contains("SELECT \\\"__j\\\".\\\"product_ref\\\" FROM \\\"order_product\\\" \\\"__j\\\" WHERE \\\"__j\\\".\\\"order_ref\\\" IN", orders);

        var products = StoreText(result, "ProductStore");
        Assert.Contains(
            "INNER JOIN \\\"order_product\\\" \\\"__j\\\" ON \\\"__j\\\".\\\"order_ref\\\" = \\\"Orders\\\".\\\"Id\\\" WHERE \\\"__j\\\".\\\"product_ref\\\" = @Id",
            products);
        Assert.Contains("_sql_Orders_Junction = \"SELECT \\\"product_ref\\\", \\\"order_ref\\\" FROM \\\"order_product\\\"", products);
        Assert.Contains("SELECT \\\"__j\\\".\\\"order_ref\\\" FROM \\\"order_product\\\" \\\"__j\\\" WHERE \\\"__j\\\".\\\"product_ref\\\" IN", products);
    }

    // Auto-managed junctions over a bounded-string key, a decimal key with precision and scale, and a
    // converter-backed key. Each synthesized column is cloned from the key it references.
    private const string NonIntegerAutoJunctionSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public readonly record struct SkuCode(string Value);

        public sealed class SkuCodeConverter : IInquiryValueConverter<SkuCode, string>
        {
            public string ToProvider(SkuCode model) => model.Value;
            public SkuCode FromProvider(string provider) => new(provider);
        }

        [InquiryTable("Codes")]
        public sealed class Code
        {
            [InquiryKey(Length = 24)] public string Name { get; set; } = string.Empty;

            [InquiryManyToMany]
            public List<Price> Prices { get; set; } = new();

            [InquiryManyToMany]
            public List<Sku> Skus { get; set; } = new();
        }

        [InquiryTable("Prices")]
        public sealed class Price
        {
            [InquiryKey(Precision = 9, Scale = 2)] public decimal Amount { get; set; }
        }

        [InquiryTable("Skus")]
        public sealed class Sku
        {
            [InquiryKey(Length = 12, IsUnicode = false, Converter = typeof(SkuCodeConverter))] public SkuCode Id { get; set; }
        }

        public partial class CodeStore : InquiryStore<Code>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Code?> GetAsync(string name, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Code> AllAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void AutoJunctionKeepsNonIntegerKeyFacetsAndForeignKeys(string dialect)
    {
        var result = RunGenerator(NonIntegerAutoJunctionSource, dialect: dialect);

        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(CompileErrors(result));
        var schema = GetSchema(result);

        // Each junction column must carry exactly the referenced key column's SQL type, and reference it.
        AssertJunctionColumnMatchesKey(schema, "Codes", "Name", "Codes_Prices", "Codes_Name");
        AssertJunctionColumnMatchesKey(schema, "Prices", "Amount", "Codes_Prices", "Prices_Amount");
        AssertJunctionColumnMatchesKey(schema, "Codes", "Name", "Codes_Skus", "Codes_Name");
        AssertJunctionColumnMatchesKey(schema, "Skus", "Id", "Codes_Skus", "Skus_Id");

        // Reads compile against the converter's model type: the dictionary is keyed by SkuCode.
        Assert.Contains("Dictionary<global::Demo.SkuCode, global::Demo.Sku>", StoreText(result, "CodeStore"));
    }

    private static void AssertJunctionColumnMatchesKey(
        string schema, string keyTable, string keyColumn, string junctionTable, string junctionColumn)
    {
        var keyType = ColumnType(schema, keyTable, keyColumn);
        Assert.Equal(keyType, ColumnType(schema, junctionTable, junctionColumn));
        var junction = CreateTableStatement(schema, junctionTable);
        Assert.Matches(
            $@"FOREIGN KEY \({QuotedName(junctionColumn)}\) REFERENCES {QuotedName(keyTable)} ?\({QuotedName(keyColumn)}\)",
            junction);
    }

    // Identifiers are quoted per dialect ("x", [x], `x`, or "X" folded); match any of them.
    private static string QuotedName(string name) => $@"[""`\[]*{name}[""`\]]*";

    private static string CreateTableStatement(string schema, string table)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            schema,
            $@"CREATE TABLE[^(]*?{QuotedName(table)}\s*\((?<body>.*?)\n\s*\)",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(match.Success, $"No CREATE TABLE for {table}.");
        return match.Groups["body"].Value;
    }

    private static string ColumnType(string schema, string table, string column)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            CreateTableStatement(schema, table),
            $@"^\s*{QuotedName(column)}\s+(?<type>.*?)(\s+PRIMARY KEY)?(\s+NOT NULL)?,?\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(match.Success, $"No column {column} in {table}.");
        return match.Groups["type"].Value.Trim();
    }
}

/// <summary>
/// A dialect without a multi-result batch. No shipped provider is one, so this is the only way to emit
/// and compile the separate-query eager path.
/// </summary>
internal sealed class NoGridInquiryGenerator: InquiryGeneratorBase
{
    protected override string Dialect => "NoGrid";
    protected override SqlBuilder CreateSqlBuilder() => new NoGridSqlBuilder();
}

internal sealed class NoGridSqlBuilder: SqlBuilder
{
    public override string DialectName => "NoGrid";
    public override string ProviderId => "nogrid";
    internal override bool SupportsMultiResultBatch => false;
    public override string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public override string BuildSelectByKeySql(SqlBuildContext context)
        => "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + AppendWhere(context.KeyWhereClause, context.ActiveRowPredicate);

    public override string BuildInsertSql(SqlBuildContext context)
        => "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ")";

    public override string BuildInsertReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpdateSql(SqlBuildContext context)
        => "UPDATE " + context.Table + " SET " + context.SetClausesWithVersion + " WHERE " + context.KeyUpdateWhereClause;

    public override string BuildUpdateReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildDeleteByKeySql(SqlBuildContext context) => "DELETE FROM " + context.Table + " WHERE " + context.KeyWriteWhereClause;
    public override string BuildUpsertSql(SqlBuildContext context) => throw new NotSupportedException();
    public override string BuildUpsertReturningSql(SqlBuildContext context) => throw new NotSupportedException();
    protected override string MapColumnType(IColumn column) => column.TypeClass == DbTypeClass.Int64 ? "INTEGER" : "TEXT";
    protected override string GeneratedKeyClause(IColumn column) => "INTEGER PRIMARY KEY";
}
