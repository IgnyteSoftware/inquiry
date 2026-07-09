using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// GroupCount emission tests: <c>[InquiryGroupCount("col")]</c> emits a
/// <c>SELECT col, COUNT(*) FROM t GROUP BY col</c> const and generates an inline
/// materializer struct for <c>GroupCount&lt;TKey&gt;</c>.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string GroupCountEntity = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TOrder")]
        public sealed class Order2
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Status")]
            public string Status { get; set; } = string.Empty;

            [InquiryColumn("Priority")]
            public int Priority { get; set; }
        }
        """;

    private static string Order2Store(string methods) =>
        GroupCountEntity + "\n\npublic partial class Order2Store : Inquiry.Stores.InquiryStore<Demo.Order2>\n{\n" + methods + "\n}\n";

    private static string GetOrder2Store(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("Order2Store.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void GroupCountEmitsGroupBySql_Sqlite()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT \\\"Status\\\", COUNT(*) FROM \\\"TOrder\\\" GROUP BY \\\"Status\\\"", text);
        Assert.Contains("_sqlGroupCount_CountByStatusAsync", text);
    }

    [Fact]
    public void GroupCountEmitsInlineMaterializer()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("_GroupCountMat_CountByStatusAsync", text);
        Assert.Contains("IInquiryEntityMaterializer<global::Inquiry.GroupCount<string>>", text);
        Assert.Contains("GetInt64(1)", text);
    }

    [Fact]
    public void GroupCountWithIntKey_Sqlite()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Priority")]
            public partial Task<IReadOnlyList<GroupCount<int>>> CountByPriorityAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT \\\"Priority\\\", COUNT(*) FROM \\\"TOrder\\\" GROUP BY \\\"Priority\\\"", text);
        Assert.Contains("GetFieldValue<int>(0)", text);
    }

    [Fact]
    public void GroupCount_SqlServer()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("SELECT [Status], COUNT(*) FROM [TOrder] GROUP BY [Status]", text);
    }

    [Fact]
    public void GroupCountUsesQueryListAsync()
    {
        var result = RunGenerator(Order2Store("""
            [InquiryGroupCount("Status")]
            public partial Task<IReadOnlyList<GroupCount<string>>> CountByStatusAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetOrder2Store(result);

        Assert.Contains("QueryListAsync", text);
    }
}
