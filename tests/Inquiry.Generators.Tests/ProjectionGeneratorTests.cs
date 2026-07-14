using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Projections: <c>[InquiryProjection(typeof(Entity))]</c> emits a materializer reading the
/// declared columns by ordinal, and a SelectAll method returning the projection selects only those
/// columns and materializes the projection type.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string CustomerProjectionSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Customer")]
        public sealed class Customer
        {
            [InquiryKey]
            public string Id { get; set; } = string.Empty;

            [InquiryColumn("CompanyName")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn("City")]
            public string? City { get; set; }
        }

        [InquiryProjection(typeof(Customer))]
        public sealed record CustomerSummary
        {
            [InquiryColumn("Id")]
            public string Id { get; init; } = string.Empty;

            [InquiryColumn("CompanyName")]
            public string Name { get; init; } = string.Empty;
        }

        public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<CustomerSummary>> ListSummariesAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ProjectionEmitsMaterializerReadingDeclaredColumnsByOrdinal()
    {
        var result = RunGenerator(CustomerProjectionSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerSummary.InquiryProjection.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("class CustomerSummaryInquiryProjectionMaterializer", text);
        Assert.Contains("readonly struct CustomerSummaryInquiryProjectionStructMaterializer", text);
        Assert.Contains("Id = reader.GetString(0)", text);
        Assert.Contains("Name = reader.GetString(1)", text);
    }

    [Fact]
    public void ProjectionSelectAllSelectsSubsetAndMaterializesProjection()
    {
        var result = RunGenerator(CustomerProjectionSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlProj_ListSummariesAsync = \"SELECT \\\"Id\\\", \\\"CompanyName\\\" FROM \\\"Customer\\\"\";", text);
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.CustomerSummary, byte, global::Demo.CustomerSummaryInquiryProjectionStructMaterializer>(", text);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlProj_ListSummariesAsync, default, static (_, _) => { })", text);
    }

    [Fact]
    public void ProjectionSelectAllByFieldSelectsSubsetWithFilter()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn("CompanyName")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("City")]
                public string? City { get; set; }
            }

            [InquiryProjection(typeof(Customer))]
            public sealed record CustomerSummary
            {
                [InquiryColumn("Id")]
                public string Id { get; init; } = string.Empty;

                [InquiryColumn("CompanyName")]
                public string Name { get; init; } = string.Empty;
            }

            public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
            {
                [InquirySelectAllByField(nameof(Customer.City))]
                public partial Task<IReadOnlyList<CustomerSummary>> ByCityAsync(string city, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // SELECT lists the projection columns; WHERE filters on the entity field column.
        Assert.Contains("private const string _sqlProj_ByCityAsync = \"SELECT \\\"Id\\\", \\\"CompanyName\\\" FROM \\\"Customer\\\" WHERE \\\"City\\\" = @City\";", text);
        // Buffered single-field fast path: QueryListAsync<TResult, TArg, TStructMat> over _sqlProj_.
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.CustomerSummary, string, global::Demo.CustomerSummaryInquiryProjectionStructMaterializer>(", text);
        Assert.Contains("_sqlProj_ByCityAsync,", text);
    }

    [Fact]
    public void OrderedProjectionByFieldBuildsPlanOverProjectionColumns()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn("CompanyName")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("City")]
                public string? City { get; set; }
            }

            [InquiryProjection(typeof(Customer))]
            public sealed record CustomerSummary
            {
                [InquiryColumn("Id")]
                public string Id { get; init; } = string.Empty;

                [InquiryColumn("CompanyName")]
                public string Name { get; init; } = string.Empty;
            }

            public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
            {
                [InquirySelectAllByField(nameof(Customer.City), OrderBy = "Name")]
                public partial Task<IReadOnlyList<CustomerSummary>> ByCityOrderedAsync(string city, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The plan const SELECTs the projection columns; the ORDER BY uses the entity column.
        Assert.Contains("private const string _sql_ByCityOrderedAsync = \"SELECT \\\"Id\\\", \\\"CompanyName\\\" FROM \\\"Customer\\\" WHERE \\\"City\\\" = @City ORDER BY \\\"CompanyName\\\" ASC\";", text);
        Assert.Contains("global::Demo.CustomerSummaryInquiryProjectionStructMaterializer", text);
    }

    [Fact]
    public void StreamingProjectionReadsEnumAndNullableByOrdinal()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public enum Status { Open, Closed }

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Status"), InquiryEnumAsString]
                public Status Status { get; set; }

                [InquiryColumn("Note")]
                public string? Note { get; set; }
            }

            [InquiryProjection(typeof(Item))]
            public sealed record ItemView
            {
                [InquiryColumn("Note")]
                public string? Note { get; init; }

                [InquiryColumn("Status"), InquiryEnumAsString]
                public Status Status { get; init; }
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquirySelectAll]
                public partial IAsyncEnumerable<ItemView> StreamViewsAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var projection = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemView.InquiryProjection.g.cs", StringComparison.Ordinal));
        var projText = projection.GetText().ToString();
        // Nullable string at ordinal 0; enum-as-string parsed at ordinal 1.
        Assert.Contains("Note = reader.IsDBNull(0) ? null : reader.GetString(0)", projText);
        Assert.Contains("Status = global::System.Enum.Parse<global::Demo.Status>(reader.GetString(1))", projText);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var storeText = store.GetText().ToString();
        Assert.Contains("private const string _sqlProj_StreamViewsAsync = \"SELECT \\\"Note\\\", \\\"Status\\\" FROM \\\"Item\\\"\";", storeText);
        // Streaming → QueryAsync, not QueryListAsync.
        Assert.Contains("Inquiry.QueryAsync<global::Demo.ItemView, byte, global::Demo.ItemViewInquiryProjectionStructMaterializer>(", storeText);
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlProj_StreamViewsAsync, default, static (_, _) => { })", storeText);
    }

    [Fact]
    public void SelectReturningUnmappedTypeReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;
            }

            public sealed class NotAProjection
            {
                public string Id { get; set; } = string.Empty;
            }

            public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<NotAProjection>> ListAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ025");
    }

    [Fact]
    public void ProjectionOfDifferentEntityReportsMismatch()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;
            }

            [InquiryTable("Vendor")]
            public sealed class Vendor
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;
            }

            [InquiryProjection(typeof(Vendor))]
            public sealed record VendorSummary
            {
                [InquiryColumn("Id")]
                public string Id { get; init; } = string.Empty;
            }

            public partial class CustomerStore : Inquiry.Stores.InquiryStore<Demo.Customer>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<VendorSummary>> ListAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ026");
    }

    // A projection over a soft-delete entity now AND-composes the entity's soft-delete filter into the
    // projection SELECT (audit P3 #14). Previously this was blocked with INQ027 because the projection
    // SqlBuildContext was built from the projection's columns — which don't carry the soft-delete flag —
    // so the filter was silently dropped. The generator now passes the entity's soft-delete column to the
    // projection context for predicate computation, while the SELECT list stays projection-only.
    private const string SoftDeleteDocSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Doc")]
        public sealed class Doc
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("IsDeleted"), InquirySoftDelete]
            public bool IsDeleted { get; set; }
        }

        [InquiryProjection(typeof(Doc))]
        public sealed record DocTitle
        {
            [InquiryColumn("Id")]
            public long Id { get; init; }

            [InquiryColumn("Title")]
            public string Title { get; init; } = string.Empty;
        }

        public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<DocTitle>> ListAsync(CancellationToken cancellationToken = default);

            [InquirySelectAll(IncludeDeleted = true)]
            public partial Task<IReadOnlyList<DocTitle>> ListAllAsync(CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ProjectionOnSoftDeleteEntityComposesSoftDeleteFilter()
    {
        var result = RunGenerator(SoftDeleteDocSource);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ027");

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // SELECT lists the projection columns; the entity's soft-delete filter is AND-composed.
        Assert.Contains("private const string _sqlProj_ListAsync = \"SELECT \\\"Id\\\", \\\"Title\\\" FROM \\\"Doc\\\" WHERE \\\"IsDeleted\\\" = 0\";", text);
    }

    [Fact]
    public void ProjectionOnSoftDeleteEntityWithIncludeDeletedOmitsFilter()
    {
        var result = RunGenerator(SoftDeleteDocSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // IncludeDeleted = true suppresses the soft-delete filter, exactly like a non-projection select.
        Assert.Contains("private const string _sqlProj_ListAllAsync = \"SELECT \\\"Id\\\", \\\"Title\\\" FROM \\\"Doc\\\"\";", text);
    }

    [Fact]
    public void ProjectionOnTimestampSoftDeleteEntityComposesIsNullFilter()
    {
        // The timestamp soft-delete kind composes "DeletedAt" IS NULL rather than "= 0"; the projection
        // path reads the indicator kind from the entity's soft-delete column, so both kinds are covered.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Title")]
                public string Title { get; set; } = string.Empty;

                [InquiryColumn("DeletedAt"), InquirySoftDelete]
                public DateTime? DeletedAt { get; set; }
            }

            [InquiryProjection(typeof(Doc))]
            public sealed record DocTitle
            {
                [InquiryColumn("Id")]
                public long Id { get; init; }

                [InquiryColumn("Title")]
                public string Title { get; init; } = string.Empty;
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<DocTitle>> ListAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlProj_ListAsync = \"SELECT \\\"Id\\\", \\\"Title\\\" FROM \\\"Doc\\\" WHERE \\\"DeletedAt\\\" IS NULL\";", text);
    }
}
