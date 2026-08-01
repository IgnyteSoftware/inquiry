using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Aggregate emission tests: <c>[InquiryCount]</c> emits a <c>SELECT COUNT(*)</c> routed through
/// the runtime scalar path, and respects the soft-delete active filter when one is declared.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerCountUsesCountBig()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            [InquiryTable("TOrg")] public sealed class Org { [InquiryKey] public long Id { get; set; } }
            public partial class OrgStore : InquiryStore<Org>
            {
                [InquiryCount] public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            }
            """;
        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("OrgStore.InquiryStore.g.cs", StringComparison.Ordinal));
        Assert.Contains("SELECT COUNT_BIG(*)", tree.GetText().ToString());
    }
    [Fact]
    public void CountEmitsScalarCountSql()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrg")]
            public sealed class Org
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class OrgStore : Inquiry.Stores.InquiryStore<Demo.Org>
            {
                [InquiryCount]
                public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OrgStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlCount = \"SELECT COUNT(*) FROM \\\"TOrg\\\"\";", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long, byte>(new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlCount, default, static (_, _) => { })", text);
    }

    [Fact]
    public void AggregateEmitsFunctionSqlAndScalarCall()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TSale")]
            public sealed class Sale
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Amount")]
                public decimal Amount { get; set; }
            }

            public partial class SaleStore : Inquiry.Stores.InquiryStore<Demo.Sale>
            {
                [InquiryAggregate(InquiryAggregateFunction.Sum, "Amount")]
                public partial Task<decimal?> SumAsync(CancellationToken cancellationToken = default);

                [InquiryAggregate(InquiryAggregateFunction.Max, "Amount")]
                public partial Task<decimal?> MaxAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("SaleStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlAgg_SumAsync = \"SELECT SUM(\\\"Amount\\\") FROM \\\"TSale\\\"\";", text);
        Assert.Contains("return Inquiry.ExecuteScalarAsync<decimal?, byte>(new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlAgg_SumAsync, default, static (_, _) => { })", text);
        Assert.Contains("private const string _sqlAgg_MaxAsync = \"SELECT MAX(\\\"Amount\\\") FROM \\\"TSale\\\"\";", text);
    }

    [Fact]
    public void CountRespectsSoftDeleteFilter()
    {
        var result = RunGenerator(WidgetStore("""
            [InquiryCount]
            public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetWidgetStore(result);

        Assert.Contains("private const string _sqlCount = \"SELECT COUNT(*) FROM \\\"TWidget\\\" WHERE \\\"IsDeleted\\\" = 0\";", text);
    }
}
