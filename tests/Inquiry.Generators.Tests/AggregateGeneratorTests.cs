using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W5 aggregate emission tests: <c>[InquiryCount]</c> emits a <c>SELECT COUNT(*)</c> routed through
/// the runtime scalar path, and respects the soft-delete active filter when one is declared.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
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
        Assert.Contains("return Inquiry.ExecuteScalarAsync<long>(new global::Inquiry.Commands.InquiryCommand(_sqlCount)", text);
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
