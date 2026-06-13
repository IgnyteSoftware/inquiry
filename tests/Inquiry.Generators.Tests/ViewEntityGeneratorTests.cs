using System;
using System.Linq;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryView]</c> maps a read-only, keyless-permitted entity: SELECT/aggregate store methods
/// generate against the view name, no key is required, the schema emitter skips it, and any
/// mutation is rejected with INQ052.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ViewSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryView("v_CustomerOrderSummary")]
        public sealed class CustomerOrderSummary
        {
            [InquiryColumn("CustomerId")]
            public string CustomerId { get; set; } = string.Empty;

            [InquiryColumn("OrderCount")]
            public int OrderCount { get; set; }

            [InquiryColumn("TotalSpent")]
            public decimal TotalSpent { get; set; }
        }

        public partial class SummaryStore : InquiryStore<CustomerOrderSummary>
        {
            [InquirySelectAll]
            public partial Task<IReadOnlyList<CustomerOrderSummary>> AllAsync(CancellationToken cancellationToken = default);

            [InquirySelectAllByField(nameof(CustomerOrderSummary.CustomerId))]
            public partial Task<IReadOnlyList<CustomerOrderSummary>> ByCustomerAsync(string customerId, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ViewStoreSelectsFromViewNameAndMaterializes()
    {
        var result = RunGenerator(ViewSource);
        AssertNoErrors(result);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("SummaryStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = store.GetText().ToString();

        // SELECTs target the view name; the keyless entity needs no key column.
        Assert.Contains("private const string _sqlSelectAll = \"SELECT \\\"CustomerId\\\", \\\"OrderCount\\\", \\\"TotalSpent\\\" FROM \\\"v_CustomerOrderSummary\\\"\";", text);
        Assert.Contains("\\\"CustomerId\\\" = @CustomerId", text);

        // The materializer is emitted exactly like a table entity's.
        Assert.Contains(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerOrderSummary.InquiryEntity.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ViewIsExcludedFromGeneratedSchemaButTableIsNot()
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

            [InquiryView("v_Summary")]
            public sealed class Summary
            {
                [InquiryColumn("Id")]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn("N")]
                public int N { get; set; }
            }

            public partial class CustomerStore : InquiryStore<Demo.Customer>
            {
                [InquirySelectAll]
                public partial Task<IReadOnlyList<Customer>> AllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var schema = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal));
        var text = schema.GetText().ToString();

        // The table gets a CREATE TABLE; the view does not (it lives in the database).
        Assert.Contains("CREATE TABLE", text);
        Assert.Contains("Customer", text);
        Assert.DoesNotContain("v_Summary", text);
    }

    [Fact]
    public void KeylessViewCompilesWithoutKeyDiagnostic()
    {
        var result = RunGenerator(ViewSource);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ001");
    }

    [Fact]
    public void MutationOnViewStoreReportsINQ052()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryView("v_Summary")]
            public sealed class Summary
            {
                [InquiryColumn("Id")]
                public string Id { get; set; } = string.Empty;
            }

            public partial class SummaryStore : InquiryStore<Demo.Summary>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Summary summary, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ052");
    }

    [Fact]
    public void StoredProcedureOnViewStoreReportsINQ052()
    {
        // A stored procedure is arbitrary SQL that can write, so it must not ride a read-only view store.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryView("v_Summary")]
            public sealed class Summary
            {
                [InquiryColumn("Id")]
                public string Id { get; set; } = string.Empty;
            }

            public partial class SummaryStore : InquiryStore<Demo.Summary>
            {
                [InquiryStoredProcedure("usp_DoStuff")]
                public partial Task<int> DoStuffAsync(int n, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ052");
    }

    [Fact]
    public void KeyBasedSelectOnKeylessViewReportsINQ053()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryView("v_Summary")]
            public sealed class Summary
            {
                [InquiryColumn("Id")]
                public string Id { get; set; } = string.Empty;
            }

            public partial class SummaryStore : InquiryStore<Demo.Summary>
            {
                [InquirySelectOneByKey]
                public partial Task<Summary?> ByKeyAsync(string id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ053");
    }
}
