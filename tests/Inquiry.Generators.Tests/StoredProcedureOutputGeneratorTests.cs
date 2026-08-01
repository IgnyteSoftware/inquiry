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

        // The static generated binder creates the input and OUTPUT parameters without an
        // InquiryParameter array. Decimal metadata prevents SqlClient rounding the read-back value.
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<string>(", text);
        Assert.Contains("static (global::System.Data.Common.DbCommand _c, string _args) =>", text);
        Assert.Contains("_p0.ParameterName = \"@category\";", text);
        Assert.Contains("_p0.Value = (object?)category ?? global::System.DBNull.Value;", text);
        Assert.Contains("_p1.ParameterName = \"@Total\";", text);
        Assert.Contains("_p1.Direction = global::System.Data.ParameterDirection.Output;", text);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Decimal;", text);
        Assert.Contains("_p1.Precision = (byte)38;", text);
        Assert.Contains("_p1.Scale = (byte)10;", text);
        Assert.Contains("global::System.Data.CommandType.StoredProcedure);", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<decimal, string>(_cmd, \"@Total\", ", text);
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
        Assert.Contains("_p1.ParameterName = \"@Name\";", text);
        Assert.Contains("_p1.Value = global::System.DBNull.Value;", text);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.String;", text);
        Assert.Contains("_p1.Direction = global::System.Data.ParameterDirection.Output;", text);
        Assert.Contains("_p1.Size = -1;", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<string?, long>(_cmd, \"@Name\", ", text);
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

        Assert.Contains("_p1.ParameterName = \"@__inquiry_return\";", text);
        Assert.Contains("_p1.Value = 0;", text);
        Assert.Contains("_p1.Direction = global::System.Data.ParameterDirection.ReturnValue;", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<int, long>(_cmd, \"@__inquiry_return\", ", text);
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
        Assert.Contains("new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(", text);
        Assert.Contains("static (global::System.Data.Common.DbCommand _c, byte _args) =>", text);
        Assert.Contains("_p0.ParameterName = \"@N\";", text);
        Assert.Contains("_p0.Value = global::System.DBNull.Value;", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int64;", text);
        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.Output;", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<long, byte>(_cmd, \"@N\", ", text);
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
