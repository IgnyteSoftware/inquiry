using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Many-to-many eager loading ([InquiryManyToMany]): the single-parent load joins the related rows
/// through the junction table (filtered by the junction's parent FK), and the batch (all-eager) load
/// assembles in memory from filtered child and junction queries. Verifies the JOIN SQL, the
/// batch consts, the per-dialect identifier quoting, and the INQ063/INQ087-INQ089 validation diagnostics.
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

    /// <summary>
    /// A composite-key related entity: <c>Tag</c> is keyed <c>(TenantId, Slug)</c> — client-supplied,
    /// since INQ011 forbids generated columns in a composite key — and the junction names one foreign-key
    /// property per key column, paired by position.
    /// </summary>
    private const string CompositeChildSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Posts")]
        public sealed class Post
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }

            [InquiryManyToMany(typeof(PostTag), nameof(PostTag.PostId), nameof(PostTag.TenantId), nameof(PostTag.Slug))]
            public List<Tag> Tags { get; set; } = new();
        }

        [InquiryTable("Tags")]
        public sealed class Tag
        {
            [InquiryKey] public int TenantId { get; set; }
            [InquiryKey(Length = 64)] public string Slug { get; set; } = string.Empty;
            [InquiryColumn] public string Label { get; set; } = string.Empty;
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
        }

        [InquiryTable("PostTag")]
        public sealed class PostTag
        {
            [InquiryKey] public long PostId { get; set; }
            [InquiryKey] public int TenantId { get; set; }
            [InquiryKey(Length = 64)] public string Slug { get; set; } = string.Empty;
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
        }

        public partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Post?> GetWithTagsAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Post> AllWithTagsAsync(CancellationToken cancellationToken = default);
        }
        """;

    private static string GetPostStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("PostStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    /// <summary>
    /// An auto-managed association: <c>[InquiryManyToMany]</c> with no arguments, so Inquiry synthesizes
    /// the junction table rather than the user mapping one.
    /// </summary>
    private const string AutoJunctionSource = """
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
            [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }

            [InquiryManyToMany]
            public List<Product> Products { get; set; } = new();
        }

        [InquiryTable("Products")]
        public sealed class Product
        {
            [InquiryKey] public long Id { get; set; }
            [InquiryColumn] public string Title { get; set; } = string.Empty;
        }

        public partial class OrderStore : Inquiry.Stores.InquiryStore<Demo.Order>
        {
            [InquirySelectOneByKeyEager]
            public partial Task<Order?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);

            [InquirySelectAllEager]
            public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);
        }
        """;

    private static string GetSchema(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

    [Fact]
    public void AutoJunctionSynthesizesATableWithBothForeignKeysAndACompositeKey_Sqlite()
    {
        var result = RunGenerator(AutoJunctionSource);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        // Names derive from both TABLE names and their key columns, so they stay stable when the CLR
        // types are renamed and unambiguous when both sides key on "Id".
        var schema = GetSchema(result);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"\"Orders_Products\"\" (", schema);
        Assert.Contains("\"\"Orders_Id\"\" INTEGER NOT NULL,", schema);
        Assert.Contains("\"\"Products_Id\"\" INTEGER NOT NULL,", schema);
        Assert.Contains("PRIMARY KEY (\"\"Orders_Id\"\", \"\"Products_Id\"\")", schema);
        Assert.Contains("FOREIGN KEY (\"\"Orders_Id\"\") REFERENCES \"\"Orders\"\"(\"\"Id\"\")", schema);
        Assert.Contains("FOREIGN KEY (\"\"Products_Id\"\") REFERENCES \"\"Products\"\"(\"\"Id\"\")", schema);
    }

    [Fact]
    public void AutoJunctionHasNoMaterializerAndIsNotRegistered_Sqlite()
    {
        var result = RunGenerator(AutoJunctionSource);

        // A synthesized junction has no CLR type. It must reach DDL and relation resolution without
        // reaching the materializer loop or DI registration, both of which name a real type.
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.Contains("Orders_Products", StringComparison.Ordinal));

        var registration = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.DoesNotContain("Orders_Products", registration);
    }

    [Fact]
    public void AutoJunctionEagerLoadJoinsThroughTheSynthesizedTable_Sqlite()
    {
        var text = GetOrderStore(RunGenerator(AutoJunctionSource));

        Assert.Contains(
            "INNER JOIN \\\"Orders_Products\\\" \\\"__j\\\" ON \\\"__j\\\".\\\"Products_Id\\\" = \\\"Products\\\".\\\"Id\\\"",
            text);

        // The batch junction read is the same projected two-column shape an explicit junction produces —
        // which is what lets the loader read it without any junction type existing.
        Assert.Contains(
            "_sql_Products_Junction = \"SELECT \\\"Orders_Id\\\", \\\"Products_Id\\\" FROM \\\"Orders_Products\\\"",
            text);
        Assert.Contains("await _grid.ReadRowsAsync(reader =>", text);
    }

    [Fact]
    public void AutoJunctionDeclaredFromBothSidesSynthesizesOneTable_Sqlite()
    {
        // The point of deriving every name from an order-independent pair. If the two sides disagreed on
        // the table name they would produce two tables; if they agreed on the name but ordered the
        // primary key differently, SchemaEmitter hashes key columns IN ORDER and would report INQ070.
        var source = AutoJunctionSource.Replace(
            """
                [InquiryColumn] public string Title { get; set; } = string.Empty;
            """,
            """
                [InquiryColumn] public string Title { get; set; } = string.Empty;

                [InquiryManyToMany]
                public List<Order> Orders { get; set; } = new();
            """);

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ070" or "INQ090");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var schema = GetSchema(result);
        Assert.Equal(
            1,
            schema.Split(new[] { "CREATE TABLE IF NOT EXISTS \"\"Orders_Products\"\"" }, StringSplitOptions.None).Length - 1);

        // Counting only the canonical name would still pass if order-independence regressed and the
        // reverse side produced a SECOND table under its own derived name — no INQ070 either, since the
        // names differ. Assert the total: exactly the two mapped tables plus one junction.
        Assert.DoesNotContain("Products_Orders", schema);
        Assert.Equal(3, schema.Split(new[] { "CREATE TABLE IF NOT EXISTS" }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AutoJunctionDeclaredOnlyFromTheOtherSideDerivesTheSameNames_Sqlite()
    {
        // Declaring from Product alone must land on the same table as declaring from Order alone —
        // otherwise adding the reverse navigation later would silently rename the table.
        var source = AutoJunctionSource
            .Replace(
                """
                    [InquiryManyToMany]
                    public List<Product> Products { get; set; } = new();
                """,
                string.Empty)
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany]
                    public List<Order> Orders { get; set; } = new();
                """)
            .Replace("[InquirySelectOneByKeyEager]\n        public partial Task<Order?> GetWithProductsAsync(long id, CancellationToken cancellationToken = default);\n\n        [InquirySelectAllEager]\n        public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);", string.Empty);

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        var schema = GetSchema(result);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"\"Orders_Products\"\" (", schema);
        Assert.Contains("PRIMARY KEY (\"\"Orders_Id\"\", \"\"Products_Id\"\")", schema);
    }

    [Fact]
    public void AutoJunctionHonoursExplicitTableAndColumnOverrides_Sqlite()
    {
        var source = AutoJunctionSource.Replace(
            "[InquiryManyToMany]",
            """[InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "order_id", ChildColumn = "product_id")]""");

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        var schema = GetSchema(result);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"\"order_product\"\" (", schema);
        Assert.Contains("PRIMARY KEY (\"\"order_id\"\", \"\"product_id\"\")", schema);
    }

    [Fact]
    public void AutoJunctionCollidingWithAMappedEntitysTableReportsINQ090()
    {
        // Merging into a mapped entity's table is the case the plan calls out as needing to be an error
        // rather than an INQ070 silent merge: that entity can carry soft-delete or global-filter columns
        // the auto read path never applies, so links would surface that its own store hides.
        var source = AutoJunctionSource.Replace(
            """
            public partial class OrderStore
            """,
            """
            [InquiryTable("Orders_Products")]
            public sealed class Legacy
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            }

            public partial class OrderStore
            """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already mapped by an entity", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Orders_Products", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoUnrelatedAutoJunctionsResolvingToOneTableReportINQ090()
    {
        // The severe case: identical overrides copy-pasted onto two unrelated associations. Merging them
        // gives one link table two meanings, and each side's eager load reads the other's rows — with
        // both key types long, nothing downstream would object.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Alpha")]
            public sealed class Alpha
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(JunctionTable = "links", ParentColumn = "left_id", ChildColumn = "right_id")]
                public List<Beta> Betas { get; set; } = new();
            }

            [InquiryTable("Beta")]
            public sealed class Beta { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("Delta")]
            public sealed class Delta
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(JunctionTable = "links", ParentColumn = "left_id", ChildColumn = "right_id")]
                public List<Epsilon> Epsilons { get; set; } = new();
            }

            [InquiryTable("Epsilon")]
            public sealed class Epsilon { [InquiryKey] public long Id { get; set; } }
            """;

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already the junction for a different pair of entities", diagnostic.GetMessage(), StringComparison.Ordinal);

        // Exactly one link table, and the rejected association contributes no eager SQL.
        var schema = GetSchema(result);
        Assert.Equal(1, schema.Split(new[] { "CREATE TABLE IF NOT EXISTS \"\"links\"\"" }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AutoJunctionAcceptsBidirectionalColumnOverridesStatedFromEachSide_Sqlite()
    {
        // ParentColumn/ChildColumn are relative to the declaring side, so the reverse navigation states
        // them swapped. Nothing covered the accepted spelling, and the docs previously said "identically"
        // — which this rejects.
        var source = AutoJunctionSource
            .Replace(
                "[InquiryManyToMany]",
                """[InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "order_id", ChildColumn = "product_id")]""")
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany(JunctionTable = "order_product", ParentColumn = "product_id", ChildColumn = "order_id")]
                    public List<Order> Orders { get; set; } = new();
                """);

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ070" or "INQ090");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var schema = GetSchema(result);
        Assert.Equal(3, schema.Split(new[] { "CREATE TABLE IF NOT EXISTS" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("PRIMARY KEY (\"\"order_id\"\", \"\"product_id\"\")", schema);
    }

    [Fact]
    public void AutoJunctionWithCaseDifferingTableOnEachSideStillGenerates_Sqlite()
    {
        // Identities compare case-insensitively — "links" and "LINKS" are one object on SQL Server,
        // MySQL, and Oracle — so the two sides can spell the table differently and only ONE entity is
        // synthesized. The reverse navigation must be rewritten with the OWNER's spelling: naming the
        // candidate's would reference an entity that does not exist, and the ordinal lookup miss took
        // down the whole generator (CS8785, zero output) rather than reporting anything.
        var source = AutoJunctionSource
            .Replace("[InquiryManyToMany]", """[InquiryManyToMany(JunctionTable = "Order_Product")]""")
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany(JunctionTable = "order_product")]
                    public List<Order> Orders { get; set; } = new();
                """);

        var result = RunGenerator(source);

        // The crash showed up as a generator-level failure with no trees at all.
        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.NotEmpty(result.RunResult.GeneratedTrees);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        // One table, and both navigations resolve to it.
        var schema = GetSchema(result);
        Assert.Equal(3, schema.Split(new[] { "CREATE TABLE IF NOT EXISTS" }, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AutoJunctionCollidingWithTheProvidersDefaultSchemaReportsINQ090()
    {
        // A null schema means "the provider's default", which spells out as dbo on SQL Server and public
        // on PostgreSQL — the same physical object. Comparing literal schema strings let an unqualified
        // junction bind to a mapped entity's table, and because SQL Server wraps CREATE TABLE in
        // IF OBJECT_ID(...) IS NULL the junction's DDL was silently skipped. Its reads then hit that
        // entity's table while applying none of its soft-delete or filter columns.
        var source = AutoJunctionSource.Replace(
            """
            public partial class OrderStore
            """,
            """
            [InquiryTable("Orders_Products", Schema = "dbo")]
            public sealed class LegacyLink
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            }

            public partial class OrderStore
            """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already mapped by an entity", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AThirdAutoNavigationFromTheReverseSideReportsINQ090()
    {
        // A pair supports one auto-managed navigation per side. Remembering only the FIRST declarer would
        // let the other side add any number of extra navigations — each compares against the first and
        // passes — so both collections would silently share one set of link rows. Whether it errored
        // would then depend on Roslyn's entity discovery order.
        var source = AutoJunctionSource.Replace(
            """
                [InquiryColumn] public string Title { get; set; } = string.Empty;
            """,
            """
                [InquiryColumn] public string Title { get; set; } = string.Empty;

                [InquiryManyToMany]
                public List<Order> Orders { get; set; } = new();

                [InquiryManyToMany]
                public List<Order> FeaturedOrders { get; set; } = new();
            """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("Each side may declare one", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoAutoJunctionsDifferingOnlyByCaseReportINQ090()
    {
        // "links" and "LINKS" are one object on SQL Server, MySQL, and Oracle, and SchemaEmitter's own
        // INQ070 grouping is ordinal — so an ordinal identity index here would emit both and let them
        // collapse at the server with no diagnostic from anywhere.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Alpha")]
            public sealed class Alpha
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(JunctionTable = "links", ParentColumn = "left_id", ChildColumn = "right_id")]
                public List<Beta> Betas { get; set; } = new();
            }

            [InquiryTable("Beta")]
            public sealed class Beta { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("Delta")]
            public sealed class Delta
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(JunctionTable = "LINKS", ParentColumn = "left_id", ChildColumn = "right_id")]
                public List<Epsilon> Epsilons { get; set; } = new();
            }

            [InquiryTable("Epsilon")]
            public sealed class Epsilon { [InquiryKey] public long Id { get; set; } }
            """;

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already the junction for a different pair of entities", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionOnANonCollectionPropertyStillReportsINQ063()
    {
        // The synthesizer stays silent on this one so it is not reported as an auto-managed-specific
        // failure — which means relation validation has to reach it BEFORE the suppression that hides
        // rejected auto relations, or it would be reported nowhere at all.
        var source = AutoJunctionSource.Replace(
            "public List<Product> Products { get; set; } = new();",
            "public Product Favourite { get; set; } = new();");

        var result = RunGenerator(source);

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ063");
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ090");
    }

    [Fact]
    public void TwoAutoJunctionsBetweenTheSamePairReportINQ090()
    {
        // Two navigations between the same entities are two associations that derive one name. Sharing
        // the table would make both collections always return identical contents.
        var source = AutoJunctionSource.Replace(
            """
                [InquiryManyToMany]
                public List<Product> Products { get; set; } = new();
            """,
            """
                [InquiryManyToMany]
                public List<Product> Products { get; set; } = new();

                [InquiryManyToMany]
                public List<Product> FeaturedProducts { get; set; } = new();
            """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already declares an auto-managed association", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Each side may declare one", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionWithAOneSidedTableOverrideReportsINQ090()
    {
        // A table-name disagreement changes the derived identity, so it cannot be caught by comparing
        // shapes that share one. Left undetected it emits TWO link tables for one association, and links
        // written through either are invisible from the other side.
        var source = AutoJunctionSource
            .Replace("[InquiryManyToMany]", """[InquiryManyToMany(JunctionTable = "order_product")]""")
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany]
                    public List<Order> Orders { get; set; } = new();
                """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("must state the same JunctionTable and JunctionSchema", diagnostic.GetMessage(), StringComparison.Ordinal);

        var schema = GetSchema(result);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS \\\"Orders_Products\\\"", schema);
    }

    [Fact]
    public void AutoJunctionCollidingWithAViewReportsINQ090()
    {
        // A view owns its name in the database exactly as a table does, and is excluded from the schema
        // set — so a CREATE TABLE against it would fail at deploy time with no compile-time signal.
        var source = AutoJunctionSource.Replace(
            """
            public partial class OrderStore
            """,
            """
            [InquiryView("Orders_Products")]
            public sealed class LegacyLinkView
            {
                [InquiryColumn] public long OrderId { get; set; }
            }

            public partial class OrderStore
            """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("already mapped by an entity", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionOverAViewReportsINQ090()
    {
        // No provider allows a foreign key referencing a view, and a synthesized junction declares one
        // to each side.
        var source = AutoJunctionSource.Replace(
            """[InquiryTable("Products")]""",
            """[InquiryView("Products")]""");

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("[InquiryView]", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("foreign key to a view", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"\"", "not a usable identifier")]
    [InlineData("\"ThisTableNameIsDeliberatelyFarTooLongToFitInsideTheSixtyThreeByteIdentifierBudget\"", "not a usable identifier")]
    public void AutoJunctionWithAnUnusableTableNameReportsINQ090(string tableName, string expected)
    {
        // Held to the same rule as an explicitly named constraint. Silent truncation is the hazard: two
        // junctions sharing a 63-byte prefix would collapse onto one physical table at the server.
        var source = AutoJunctionSource.Replace(
            "[InquiryManyToMany]",
            $"[InquiryManyToMany(JunctionTable = {tableName})]");

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains(expected, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionAcrossDifferentSchemasReportsINQ090()
    {
        // Picking one silently would place the table in whichever schema sorts first, and move it if
        // either were ever renamed.
        var source = AutoJunctionSource
            .Replace("""[InquiryTable("Orders")]""", """[InquiryTable("Orders", Schema = "sales")]""")
            .Replace("""[InquiryTable("Products")]""", """[InquiryTable("Products", Schema = "catalog")]""");

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("different schemas", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Set JunctionSchema on both sides", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedAutoJunctionReportsOnlyINQ090()
    {
        // A rejected auto relation still carries no junction name, which would otherwise make relation
        // validation add INQ087 naming a junction type "(unknown)" the user never wrote.
        var source = AutoJunctionSource.Replace(
            """[InquiryTable("Products")]""",
            """[InquiryView("Products")]""");

        var result = RunGenerator(source);

        Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ063" or "INQ087" or "INQ088" or "INQ089");
    }

    [Fact]
    public void AutoJunctionOnASelfReferentialAssociationReportsINQ090()
    {
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Employees")]
            public sealed class Employee
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany]
                public List<Employee> Reports { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("self-referential", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionOnACompositeKeyEntityReportsINQ090()
    {
        // One column per side cannot express a composite key; the explicit three-argument form can.
        var source = AutoJunctionSource.Replace(
            "[InquiryColumn] public string Title { get; set; } = string.Empty;",
            "[InquiryKey] public int TenantId { get; set; }");

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("composite primary key", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("three-argument constructor", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoJunctionWithDisagreeingOverridesOnEachSideReportsINQ090()
    {
        // Both sides describe the same table; only one states the column overrides. Taking the
        // first-seen shape would make the generated table depend on entity discovery order.
        var source = AutoJunctionSource
            .Replace(
                "[InquiryManyToMany]",
                """[InquiryManyToMany(ParentColumn = "order_id", ChildColumn = "product_id")]""")
            .Replace(
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;
                """,
                """
                    [InquiryColumn] public string Title { get; set; } = string.Empty;

                    [InquiryManyToMany]
                    public List<Order> Orders { get; set; } = new();
                """);

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ090"));
        Assert.Contains("the reverse side states the two swapped", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>Returns the single line declaring <paramref name="constName"/>, for per-const assertions.</summary>
    private static string ConstLine(string text, string constName)
        => Assert.Single(
            text.Split('\n'),
            line => line.Contains(" " + constName + " = ", StringComparison.Ordinal));

    [Theory]
    [InlineData("Sqlite", "\"", "\"", "\"__j\"", "\"__c\"")]
    [InlineData("SqlServer", "[", "]", "[__j]", "[__c]")]
    [InlineData("PostgreSql", "\"", "\"", "\"__j\"", "\"__c\"")]
    [InlineData("MySql", "`", "`", "`__j`", "`__c`")]
    [InlineData("MariaDb", "`", "`", "`__j`", "`__c`")]
    [InlineData("Oracle", "", "", "\"__j\"", "\"__c\"")]
    public void CompositeKeyChildManyToManyCorrelatesByAliasNotSchemaQualifiedTable(
        string dialect, string quoteOpen, string quoteClose, string junctionAlias, string childAlias)
    {
        // The composite branches introduce aliases precisely so a correlation never has to name the
        // table. With a schema in play, qualifying a column as "app"."Tags"."TenantId" is what
        // ManyToManyBatchChildSqlSupportsSchemasWithoutQualifiedColumnReferences forbids. Scoped to the
        // two batch consts: the single-parent JOIN qualifies its select list with the child table by
        // design, and that predates composite support.
        var source = CompositeChildSource
            .Replace("[InquiryTable(\"Posts\")]", "[InquiryTable(\"Posts\", Schema = \"app\")]")
            .Replace("[InquiryTable(\"Tags\")]", "[InquiryTable(\"Tags\", Schema = \"app\")]")
            .Replace("[InquiryTable(\"PostTag\")]", "[InquiryTable(\"PostTag\", Schema = \"app\")]");

        var result = RunGenerator(source, dialect: dialect);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetPostStore(result);
        string Q(string name) => quoteOpen + name + quoteClose;

        foreach (var constName in new[] { "_sql_Tags_All", "_sql_Tags_Junction" })
        {
            var line = ConstLine(text, constName);
            Assert.Contains(
                EscapeGeneratedString(junctionAlias + "." + Q("TenantId") + " = " + childAlias + "." + Q("TenantId")),
                line);
            Assert.DoesNotContain(EscapeGeneratedString(Q("app") + "." + Q("Tags") + "." + Q("TenantId")), line);
            Assert.DoesNotContain(EscapeGeneratedString(Q("app") + "." + Q("PostTag") + "." + Q("TenantId")), line);
        }
    }

    [Fact]
    public void CompositeKeyChildManyToManyIsAcceptedAndCompiles_Sqlite()
    {
        var result = RunGenerator(CompositeChildSource);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ063" or "INQ087" or "INQ088" or "INQ089");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void CompositeKeyChildManyToManyJoinsOnEveryKeyColumn_Sqlite()
    {
        var text = GetPostStore(RunGenerator(CompositeChildSource));

        // The single-parent JOIN pairs each junction foreign key with its child key column.
        Assert.Contains(
            "ON \\\"__j\\\".\\\"TenantId\\\" = \\\"Tags\\\".\\\"TenantId\\\" AND \\\"__j\\\".\\\"Slug\\\" = \\\"Tags\\\".\\\"Slug\\\"",
            text);
    }

    [Fact]
    public void CompositeKeyChildManyToManyCorrelatesWithExistsNotRowValueIn_Sqlite()
    {
        var text = GetPostStore(RunGenerator(CompositeChildSource));

        // A row-value IN — (a, b) IN (SELECT x, y …) — is not portable: SQL Server has no row-value
        // constructors at any version. Both batch selects correlate with EXISTS instead, which every
        // dialect plans as the same semi-join.
        Assert.Contains("_sql_Tags_All = \"SELECT", text);
        Assert.Contains("EXISTS (SELECT 1 FROM", text);

        // The outer child is aliased so the correlation qualifier is a bare alias, never a table name.
        Assert.Contains("FROM \\\"Tags\\\" \\\"__c\\\" WHERE", text);
        Assert.Contains("\\\"__j\\\".\\\"TenantId\\\" = \\\"__c\\\".\\\"TenantId\\\" AND \\\"__j\\\".\\\"Slug\\\" = \\\"__c\\\".\\\"Slug\\\"", text);

        // The junction batch select aliases its own outer table too: inside its EXISTS the child is in
        // scope, so an unqualified junction foreign key would bind to the child's same-named column.
        Assert.Contains("_sql_Tags_Junction = \"SELECT \\\"PostId\\\", \\\"TenantId\\\", \\\"Slug\\\" FROM \\\"PostTag\\\" \\\"__j\\\" WHERE", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void CompositeKeyChildManyToManyEmitsNoRowValueInOnAnyDialect(string dialect)
    {
        var result = RunGenerator(CompositeChildSource, dialect: dialect);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetPostStore(result);

        Assert.Contains("EXISTS (SELECT 1 FROM", text);
        Assert.DoesNotContain(") IN (SELECT", text);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    // Transposed: the junction names the child's key columns in the wrong order. Their types differ, so
    // the mismatch is catchable — a same-typed transposition is not, which is why order is documented.
    [InlineData("nameof(PostTag.Slug), nameof(PostTag.TenantId)")]
    // Duplicated: one child key column named twice, leaving the other uncorrelated.
    [InlineData("nameof(PostTag.TenantId), nameof(PostTag.TenantId)")]
    // Right arity, but a property that is not a key column of the child at that position.
    [InlineData("nameof(PostTag.TenantId), nameof(PostTag.PostId)")]
    public void CompositeKeyChildManyToManyRejectsMispairedForeignKeys(string foreignKeys)
    {
        // A mis-paired list is not a compile error — both the SQL correlation and the tuple lookup follow
        // the same wrong pairing — so it would silently join a child row to the wrong parent. With a
        // global-filter column among the key components that is a cross-tenant read, so it has to be
        // rejected at generation time rather than discovered in production.
        var source = CompositeChildSource.Replace(
            "nameof(PostTag.TenantId), nameof(PostTag.Slug)",
            foreignKeys);

        var result = RunGenerator(source);

        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ089");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.GetText().ToString().Contains("EXISTS (SELECT 1 FROM", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositeKeyChildManyToManyRejectsDuplicateForeignKeyWhenTypesCannotDistinguishIt()
    {
        // When both child key columns share a type, naming one of them twice passes the type-pairing
        // check — nothing about the types is wrong. Only the distinctness rule catches it, and without
        // that the second key column would go uncorrelated: every child sharing the first component
        // would match, which for a tenant discriminator is a cross-tenant read.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Posts")]
            public sealed class Post
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(typeof(PostTag), nameof(PostTag.PostId), nameof(PostTag.TenantId), nameof(PostTag.TenantId))]
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
                [InquiryKey] public long PostId { get; set; }
                [InquiryKey] public int TenantId { get; set; }
                [InquiryKey] public int CategoryId { get; set; }
            }

            public partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Post> AllWithTagsAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ089");
        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.GetText().ToString().Contains("EXISTS (SELECT 1 FROM", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositeKeyChildManyToManyGuardsEveryNullableJunctionComponent()
    {
        // A nullable junction foreign key against a non-nullable child key column: the pairing rule
        // compares non-nullable types, so this is accepted. Each nullable component then needs its own
        // null skip emitted BEFORE the tuple is built — a tuple component cannot be nullable, and
        // dereferencing an unguarded null would throw once per row.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Posts")]
            public sealed class Post
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(typeof(PostTag), nameof(PostTag.PostId), nameof(PostTag.TenantId), nameof(PostTag.Slug))]
                public List<Tag> Tags { get; set; } = new();
            }

            [InquiryTable("Tags")]
            public sealed class Tag
            {
                [InquiryKey] public int TenantId { get; set; }
                [InquiryKey(Length = 64)] public string Slug { get; set; } = string.Empty;
            }

            [InquiryTable("PostTag")]
            public sealed class PostTag
            {
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn] public long PostId { get; set; }
                [InquiryColumn] public int? TenantId { get; set; }
                [InquiryColumn(Length = 64)] public string? Slug { get; set; }
            }

            public partial class PostStore : Inquiry.Stores.InquiryStore<Demo.Post>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Post> AllWithTagsAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ063" or "INQ087" or "INQ088" or "INQ089");
        var text = GetPostStore(result);

        // Each component is read into a local of its OWN (nullable) type, guarded, then unwrapped.
        Assert.Contains("int? _jChildKey0 = ", text);
        Assert.Contains("string? _jChildKey1 = ", text);
        Assert.Contains("if (_jChildKey0 is null) return;", text);
        Assert.Contains("if (_jChildKey1 is null) return;", text);
        Assert.Contains("TryGetValue((_jChildKey0.Value, _jChildKey1!), out var _child)", text);

        // The whole point: nullable components must not leak a warning or error into generated code.
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(
            result.Compilation.GetDiagnostics(),
            static d => d.Severity == DiagnosticSeverity.Warning && d.Id is "CS8600" or "CS8601" or "CS8602");
    }

    [Fact]
    public void SingleKeyManyToManyStillAcceptsAWideningForeignKeyType()
    {
        // A junction foreign key narrower than the child key it references has always worked: the SQL
        // comparison is fine on all six dialects and Dictionary<long, T>.TryGetValue(int) binds by
        // implicit widening. Composite support added a type-pairing rule to catch transposed key lists,
        // and that rule must not reach back and reject this — there is nothing to transpose at arity 1.
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(typeof(OrderProduct), nameof(OrderProduct.OrderId), nameof(OrderProduct.ProductId))]
                public List<Product> Products { get; set; } = new();
            }

            [InquiryTable("Products")]
            public sealed class Product { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("OrderProduct")]
            public sealed class OrderProduct
            {
                [InquiryKey] public long OrderId { get; set; }
                [InquiryKey] public int ProductId { get; set; }
            }

            public partial class OrderStore : Inquiry.Stores.InquiryStore<Demo.Order>
            {
                [InquirySelectAllEager]
                public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id is "INQ063" or "INQ087" or "INQ088" or "INQ089");
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void CompositeKeyChildManyToManyIndexesChildrenByValueTuple_Sqlite()
    {
        var text = GetPostStore(RunGenerator(CompositeChildSource));

        // A value tuple brings structural equality and hashing with it, so no IEqualityComparer is
        // needed. The junction read builds the same shape to look the child up.
        Assert.Contains("_childByKey_Tags = new global::System.Collections.Generic.Dictionary<(int, string), global::Demo.Tag>", text);
        Assert.Contains("_childByKey_Tags[(_c.TenantId, _c.Slug)] = _c;", text);
        Assert.Contains("_childByKey_Tags.TryGetValue((_jChildKey0, _jChildKey1), out var _child)", text);

        // Both junction key components are read off the reader in ascending ordinal order, after the
        // parent foreign key at ordinal 0 — the grid reads with SequentialAccess.
        Assert.Contains("long _jParentKey = reader.GetInt64(0);", text);
        Assert.Contains("int _jChildKey0 = reader.GetInt32(1);", text);
        Assert.Contains("string _jChildKey1 = reader.GetString(2);", text);
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
        // The junction SELECT is projected to just the two foreign keys — the batch loader groups child
        // keys under parent keys and reads nothing else off a junction row. The soft-delete and
        // global-filter columns leave the select list but must stay in the WHERE clause.
        Assert.Contains("_sql_Products_Junction = \"SELECT \\\"OrderId\\\", \\\"ProductId\\\" FROM \\\"OrderProduct\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1 AND \\\"OrderId\\\" IN (SELECT \\\"Id\\\" FROM \\\"Orders\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1) AND \\\"ProductId\\\" IN (SELECT \\\"Id\\\" FROM \\\"Products\\\" WHERE \\\"IsDeleted\\\" = 0 AND \\\"IsActive\\\" = 1)\";", text);
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

        // The junction rows are read straight off the grid's reader — the batch loader wants two scalars
        // per row, so nothing is materialized. The junction's own materializer stays unreferenced, which
        // is the assertion that would break if the read ever regressed to ReadForEachAsync.
        Assert.Contains("await _grid.ReadRowsAsync(reader =>", text);
        Assert.DoesNotContain("OrderProductInquiryEntityStructMaterializer", text);

        // Both keys are hoisted before either is used, in ascending ordinal order: the grid reads with
        // SequentialAccess, and the parent key below is touched three times. Ordinal 0 is the parent FK
        // and 1 the child FK, matching the projected select list.
        Assert.Contains(
            """
                        long _jParentKey = reader.GetInt64(0);
                        long _jChildKey = reader.GetInt64(1);
            """.TrimStart(),
            text);
    }

    [Fact]
    public void ManyToManyJunctionRawReadToleratesAUserParameterNamedReader_Sqlite()
    {
        // Every other identifier the eager emitter introduces is underscore-prefixed so it cannot
        // collide with a user's parameter name. The junction read's lambda parameter is the exception —
        // it must be `reader`, because MaterializerEmitter.ReadExpression hard-codes that receiver — so
        // a user parameter of the same name lands in the enclosing scope. Shadowing it is legal, but
        // nothing else in the emitter guarantees that stays true: a future change that introduced a
        // `reader` local inside the lambda instead would be a compile error only in this shape.
        var source = OrderProductSource.Replace(
            "public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken cancellationToken = default);",
            "public partial IAsyncEnumerable<Order> AllWithProductsAsync(CancellationToken reader = default);");

        var result = RunGenerator(source);
        Assert.Empty(result.GeneratorDiagnostics);

        var text = GetOrderStore(result);
        Assert.Contains("AllWithProductsAsync([global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken reader)", text);
        Assert.Contains("await _grid.ReadRowsAsync(reader =>", text);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ManyToManyJunctionRawReadAppliesValueConverter_Sqlite()
    {
        // The raw junction read bypasses the junction materializer, so it has to reproduce what the
        // materializer did for these two columns. A converter on a junction FK is the case that fails
        // silently: reader.GetInt64 alone compiles wherever the model type is long, and simply skips the
        // conversion — which for a converted key means looking the child up under the wrong value.
        var source = OrderProductSource
            .Replace(
                "[InquiryKey] public long ProductId { get; set; }",
                "[InquiryKey(Converter = typeof(ProductIdConverter))] public long ProductId { get; set; }")
            .Replace(
                "[InquiryTable(\"OrderProduct\")]",
                """
                public sealed class ProductIdConverter : IInquiryValueConverter<long, long>
                {
                    public long ToProvider(long model) => model;
                    public long FromProvider(long provider) => provider;
                }

                [InquiryTable("OrderProduct")]
                """);

        var result = RunGenerator(source);
        Assert.Empty(result.GeneratorDiagnostics);
        var text = GetOrderStore(result);

        Assert.Contains("long _jChildKey = ", text);
        Assert.Contains("ProductIdConverter", text);
        Assert.DoesNotContain("long _jChildKey = reader.GetInt64(1);", text);
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
        // SelectAllEager orders the child sets FIRST and the parent LAST (#70), so parents stream out of
        // the reader instead of being buffered. A relation's _All set stays immediately before its
        // _Junction set — the grouping reads depend on that pairing.
        Assert.Contains("var _sql = \"DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR \" + _sql_Products_All + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sql_Products_Junction + \"; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR \" + _sqlSelectAll + \"; DBMS_SQL.RETURN_RESULT(c); END;\";", text);
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
    public void ManyToManyWithNoChildForeignKeyNameReportsINQ063OnACollectionProperty()
    {
        // Discovery records a malformed attribute the same way it records a non-collection property —
        // IsCollection forced false — so INQ063 cannot tell the two apart. Its message therefore names
        // both causes rather than asserting "this is not a collection" about a List<T>, which is what a
        // narrower wording would do here and would be simply false.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                // params string[] makes zero child foreign-key names compile.
                [InquiryManyToMany(typeof(OrderProduct), "OrderId")]
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

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ063"));
        var message = diagnostic.GetMessage();
        Assert.Contains("at least one child foreign-key property name", message, StringComparison.Ordinal);
        Assert.DoesNotContain("it is not a collection navigation", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyToManyWithUnmappedJunctionTypeReportsINQ087NamingTheType()
    {
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(typeof(OrderProduct), "OrderId", "ProductId")]
                public List<Product> Products { get; set; } = new();
            }

            [InquiryTable("Products")]
            public sealed class Product { [InquiryKey] public long Id { get; set; } }

            // No [InquiryTable]: not a mapped entity.
            public sealed class OrderProduct
            {
                public long OrderId { get; set; }
                public long ProductId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ087"));
        Assert.Contains("OrderProduct", diagnostic.GetMessage(), StringComparison.Ordinal);

        // The old catch-all fired here too; the split is only worth it if the narrower code replaces it
        // rather than joining it, otherwise a user still has to read every reason to find the real one.
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ063");
    }

    [Fact]
    public void CompositeKeyChildManyToManyTranspositionMessageNamesTheMismatchedPair()
    {
        // INQ089's trailing sentence is what turns "these do not pair" into something actionable: with
        // the counts equal, it names the first position whose types disagree and says the order may be
        // wrong — which is the actual mistake behind almost every transposition.
        var source = CompositeChildSource.Replace(
            "nameof(PostTag.TenantId), nameof(PostTag.Slug)",
            "nameof(PostTag.Slug), nameof(PostTag.TenantId)");

        var result = RunGenerator(source);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ089"));
        var message = diagnostic.GetMessage();
        Assert.Contains("'Slug'", message, StringComparison.Ordinal);
        Assert.Contains("'TenantId'", message, StringComparison.Ordinal);
        Assert.Contains("the names may be in the wrong order", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManyToManyWithUnknownJunctionForeignKeyReportsINQ088()
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

        // INQ088 rather than the old catch-all: the point of splitting it out is that the message can
        // name the string that did not resolve, which is the whole content of a typo.
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ088"));
        Assert.Contains("'Nope'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("OrderProduct", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ManyToManyNamingMoreThanOneChildForeignKeyForASingleKeyChildReportsINQ089()
    {
        // The child foreign-key parameter is `params string[]`, so naming several columns compiles.
        // The arity must match the related entity's key column count, and that check lives at validation
        // rather than at discovery because that is where the related entity's key count is known —
        // discovery sees one entity symbol at a time. This pins that a wrong arity is rejected, and
        // that it is rejected without generating any join SQL from the first name alone.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Orders")]
            public sealed class Order
            {
                [InquiryKey] public long Id { get; set; }

                [InquiryManyToMany(typeof(OrderProduct), "OrderId", "ProductId", "RegionId")]
                public List<Product> Products { get; set; } = new();
            }

            [InquiryTable("Products")]
            public sealed class Product { [InquiryKey] public long Id { get; set; } }

            [InquiryTable("OrderProduct")]
            public sealed class OrderProduct
            {
                [InquiryKey] public long OrderId { get; set; }
                [InquiryKey] public long ProductId { get; set; }
                [InquiryColumn] public long RegionId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));

        // INQ089 states both counts, so the message alone says what to change.
        var diagnostic = Assert.Single(result.RunResult.Diagnostics.Where(static d => d.Id == "INQ089"));
        Assert.Contains("names 2 child foreign-key properties", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("which has 1 key column", diagnostic.GetMessage(), StringComparison.Ordinal);

        Assert.DoesNotContain(
            result.RunResult.GeneratedTrees,
            static tree => tree.GetText().ToString().Contains("INNER JOIN", StringComparison.Ordinal));
    }
}
