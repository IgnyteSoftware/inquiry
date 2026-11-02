using System.Globalization;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryStoredProcedure]</c> with a <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c> return buffers
/// one result set like an ordinary select. Rows do not combine with a scalar read-back (INQ051).
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ProcListHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Item")]
        public sealed class Item
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        public partial class ProcListStore : InquiryStore<Item>
        {
        """;

    private static string GetProcListStore(GeneratorTestResult result)
        => Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ProcListStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

    [Fact]
    public void ProcList_BuffersRowsWithNameMappedMaterializer()
    {
        var source = ProcListHeader + """
                [InquiryStoredProcedure("usp_ItemsByName")]
                public partial Task<IReadOnlyList<Item>> ByNameAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetProcListStore(result);

        Assert.Contains("_p0.ParameterName = \"@name\";", text);
        Assert.Contains("global::System.Data.CommandType.StoredProcedure);", text);
        Assert.Contains("return await Inquiry.QueryListAsync<global::Demo.Item, string, global::Demo.ItemInquiryEntityNameMappedMaterializer>(_cmd, default, cancellationToken).ConfigureAwait(false);", text);
    }

    [Fact]
    public void ProcList_OracleWrapsInRefCursorBlock()
    {
        var source = ProcListHeader + """
                [InquiryStoredProcedure("GET_ITEMS")]
                public partial Task<IReadOnlyList<Item>> GetItemsAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetProcListStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR; BEGIN GET_ITEMS(:", text);
        Assert.Contains(", c0); DBMS_SQL.RETURN_RESULT(c0); END;", text);
        Assert.Contains("global::System.Data.CommandType.Text);", text);
        Assert.DoesNotContain("CommandType.StoredProcedure", text);
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.Item, string, global::Demo.ItemInquiryEntityNameMappedMaterializer>(_cmd, default, cancellationToken)", text);
    }

    [Fact]
    public void ProcList_SqlServerTvpParameterBindsTable()
    {
        var source = ProcListHeader + """
                [InquiryStoredProcedure("usp_ItemsByIds")]
                public partial Task<IReadOnlyList<Item>> ByIdsAsync(
                    [InquiryParameter(TvpTypeName = "[dbo].[IntList]")] IEnumerable<int> ids,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetProcListStore(result);

        Assert.Contains("InquiryTvpParameter.Bind(_c, \"@ids\", ids, \"[dbo].[IntList]\", _sprocTvp_ByIdsAsync_ids);", text);
        Assert.Contains("Inquiry.QueryListAsync<global::Demo.Item,", text);
    }

    [Theory]
    [InlineData("OutputParameter = \"Total\"")]
    [InlineData("ReturnsValue = true")]
    public void ProcList_WithScalarReadBackReportsINQ051(string readBack)
    {
        var source = ProcListHeader + $$"""
                [InquiryStoredProcedure("usp_X", {{readBack}})]
                public partial Task<IReadOnlyList<Item>> XAsync(CancellationToken cancellationToken = default);

                [InquiryStoredProcedure("usp_Y", {{readBack}})]
                public partial Task<IReadOnlyList<byte[]>> YAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var diagnostics = result.RunResult.Diagnostics.Where(static d => d.Id == "INQ051").ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, static d => Assert.Contains("Task<IReadOnlyList<T>> row result", d.GetMessage(CultureInfo.InvariantCulture)));
        Assert.DoesNotContain(result.RunResult.Diagnostics, static d => d.Id == "INQ005");
    }

    [Fact]
    public void ProcList_WithInOutParameterReportsINQ051()
    {
        var source = ProcListHeader + """
                [InquiryStoredProcedure("usp_X")]
                public partial Task<IReadOnlyList<Item>> XAsync([InquiryParameter(IsInputOutput = true)] int counter, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ051");
        Assert.Contains("IsInputOutput cannot be combined with a Task<IReadOnlyList<T>> row result", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }
}
