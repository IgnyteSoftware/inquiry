using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryStoredProcedure]</c> scalar output: <c>OutputParameter</c> binds an OUTPUT parameter
/// and surfaces its read-back value as the <c>Task&lt;TScalar&gt;</c> result; <c>ReturnsValue</c>
/// surfaces the integer RETURN value. Misconfiguration is INQ051.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ProcStoreHeader = """
        using System;
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
        }

        public partial class ItemStore : InquiryStore<Item>
        {
        """;

    [Fact]
    public void OutputParameterBindsOutputDirectionAndReadsItBack()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_SumPrices", OutputParameter = "Total")]
                public partial Task<decimal> SumPricesAsync(string category, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Input param, then the OUTPUT param with DbType + Output direction. A decimal output
        // stamps precision/scale so SqlClient doesn't round the read-back value to scale 0.
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@category\", (object?)category ?? global::System.DBNull.Value),", text);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Total\", global::System.DBNull.Value, dbType: global::System.Data.DbType.Decimal, direction: global::System.Data.ParameterDirection.Output, precision: (byte)38, scale: (byte)10),", text);
        Assert.Contains("global::System.Data.CommandType.StoredProcedure);", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<decimal>(_cmd, \"@Total\", ", text);
    }

    [Fact]
    public void StringOutputParameterStampsMaxSize()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_GetName", OutputParameter = "@Name")]
                public partial Task<string?> GetNameAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        // An explicit @-prefixed name is kept; string output gets Size = -1.
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@Name\", global::System.DBNull.Value, dbType: global::System.Data.DbType.String, direction: global::System.Data.ParameterDirection.Output, size: -1),", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<string?>(_cmd, \"@Name\", ", text);
    }

    [Fact]
    public void ReturnsValueBindsReturnDirectionAndReadsInt()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_Validate", ReturnsValue = true)]
                public partial Task<int> ValidateAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@__inquiry_return\", 0, direction: global::System.Data.ParameterDirection.ReturnValue),", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<int>(_cmd, \"@__inquiry_return\", ", text);
    }

    [Fact]
    public void NoInputParametersStillEmitsOutputParameterArray()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_Count", OutputParameter = "N")]
                public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.DoesNotContain("global::System.Array.Empty<global::Inquiry.Parameters.InquiryParameter>()", text);
        Assert.Contains("new global::Inquiry.Parameters.InquiryParameter(\"@N\", global::System.DBNull.Value, dbType: global::System.Data.DbType.Int64, direction: global::System.Data.ParameterDirection.Output),", text);
    }

    [Fact]
    public void OutputParameterAndReturnsValueTogetherReportINQ051()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_X", OutputParameter = "Total", ReturnsValue = true)]
                public partial Task<int> XAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ051");
    }

    [Fact]
    public void ReturnsValueOnNonIntReportsINQ051()
    {
        var source = ProcStoreHeader + """
                [InquiryStoredProcedure("usp_X", ReturnsValue = true)]
                public partial Task<decimal> XAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ051");
    }
}
