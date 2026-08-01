using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Multi-result-set stored procedures return <c>Task&lt;(IReadOnlyList&lt;A&gt;, IReadOnlyList&lt;B&gt;, …)&gt;</c>
/// and emit <c>QueryMultipleAsync</c> + <c>InquiryGridReader.ReadListAsync</c> per result set.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string MultiResultHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Order")]
        public sealed class Order
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("CustomerId")]
            public long CustomerId { get; set; }
        }

        [InquiryTable("OrderLine")]
        public sealed class OrderLine
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("OrderId")]
            public long OrderId { get; set; }

            [InquiryColumn("ProductName")]
            public string ProductName { get; set; } = string.Empty;
        }

        [InquiryTable("Customer")]
        public sealed class Customer
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        """;

    private static string GetMultiResultStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("MultiResultStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void MultiResult_TwoResultSetsEmitsGridReader()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetOrdersAndLines")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetOrdersAndLinesAsync(
                    long customerId, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("QueryMultipleAsync<", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.Order,", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.OrderLine,", text);
        Assert.Contains("return (_r0, _r1);", text);
        Assert.Contains("global::System.Data.CommandType.StoredProcedure", text);
    }

    [Fact]
    public void MultiResult_ThreeResultSetsEmitsAllReads()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetAll")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>, IReadOnlyList<Customer>)> GetAllAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("_grid.ReadListAsync<global::Demo.Order,", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.OrderLine,", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.Customer,", text);
        Assert.Contains("return (_r0, _r1, _r2);", text);
    }

    [Fact]
    public void MultiResult_WithInputParametersBindsCorrectly()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetOrdersAndLines")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetByCustomerAsync(
                    long customerId, string status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("_p0.ParameterName = \"@customerId\";", text);
        Assert.Contains("_p1.ParameterName = \"@status\";", text);
        Assert.Contains("QueryMultipleAsync<", text);
        Assert.Contains("return (_r0, _r1);", text);
    }

    [Fact]
    public void MultiResult_NoParametersUsesEmptyState()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetAll")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetAllAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("QueryMultipleAsync<", text);
        Assert.Contains("return (_r0, _r1);", text);
    }

    [Fact]
    public void MultiResult_SameEntityTypeInMultipleResultSets()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetPendingAndShipped")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<Order>)> GetPendingAndShippedAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        var firstRead = text.IndexOf("_grid.ReadListAsync<global::Demo.Order,", StringComparison.Ordinal);
        var secondRead = text.IndexOf("_grid.ReadListAsync<global::Demo.Order,", firstRead + 1, StringComparison.Ordinal);
        Assert.True(firstRead >= 0, "First Order read should exist");
        Assert.True(secondRead > firstRead, "Second Order read should exist");
        Assert.Contains("return (_r0, _r1);", text);
    }

    [Fact]
    public void MultiResult_NoSeparateOutputParameterEmitted()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetAll")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetAllAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.DoesNotContain("ParameterDirection.Output", text);
        Assert.DoesNotContain("ParameterDirection.ReturnValue", text);
        Assert.DoesNotContain("ExecuteProcedureScalarAsync", text);
        Assert.DoesNotContain("ExecuteAsync", text);
    }

    [Fact]
    public void MultiResult_UsesAwaitUsingForGridDisposal()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetAll")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> GetAllAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("await using var _grid =", text);
    }

    [Fact]
    public void MultiResult_UnmappedEntityTypeRejected()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Order")]
            public sealed class Order
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public sealed class UnmappedDto
            {
                public string Name { get; set; } = string.Empty;
            }

            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_Bad")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<UnmappedDto>)> BadAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ051" && d.GetMessage().Contains("UnmappedDto"));
    }

    [Fact]
    public void MultiResult_NonIReadOnlyListElementRejectedAsUnsupportedReturnType()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_Bad")]
                public partial Task<(IReadOnlyList<Order>, List<OrderLine>)> BadAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ005");
    }

    [Fact]
    public void MultiResult_OutputParameterWithTupleReturnRejected()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_Bad", OutputParameter = "Total")]
                public partial Task<(IReadOnlyList<Order>, IReadOnlyList<OrderLine>)> BadAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ051");
    }

    [Fact]
    public void MultiResult_NamedTupleElementsWork()
    {
        var source = MultiResultHeader + """
            public partial class MultiResultStore : InquiryStore<Order>
            {
                [InquiryStoredProcedure("usp_GetAll")]
                public partial Task<(IReadOnlyList<Order> Orders, IReadOnlyList<OrderLine> Lines)> GetAllAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetMultiResultStore(result);

        Assert.Contains("QueryMultipleAsync<", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.Order,", text);
        Assert.Contains("_grid.ReadListAsync<global::Demo.OrderLine,", text);
    }
}
