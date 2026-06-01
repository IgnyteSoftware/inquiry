using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W3b batch-update emission: <c>[InquiryUpdateAll]</c> emits a per-row UPDATE template (with a
/// <c>{r}</c> row token) and a binder writing <c>@u{r}_&lt;n&gt;</c> / <c>@u{r}_k&lt;n&gt;</c> params.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void UpdateAllEmitsPerRowTemplateAndBinder()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("Qty")]
                public int Qty { get; set; }
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryUpdateAll]
                public partial Task<int> UpdateAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Key is generated → excluded from SET, used in WHERE.
        Assert.Contains("private const string _sqlUpdateAllRow = \"UPDATE \\\"TThing\\\" SET \\\"Name\\\" = @u{r}_0, \\\"Qty\\\" = @u{r}_1 WHERE \\\"Id\\\" = @u{r}_k0;\";", text);
        Assert.Contains("_sb.Append(_sqlUpdateAllRow.Replace(\"{r}\", _r.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));", text);
        Assert.Contains("_p.ParameterName = \"@u\" + _r + \"_0\";", text);
        Assert.Contains("_p.ParameterName = \"@u\" + _r + \"_k0\";", text);
    }

    [Fact]
    public void UpdateAllCompositeKeyAndsAllKeyColumns()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("OrderLine")]
            public sealed class OrderLine
            {
                [InquiryKey("OrderId")]
                public long OrderId { get; set; }

                [InquiryKey("ProductId")]
                public long ProductId { get; set; }

                [InquiryColumn("Qty")]
                public int Qty { get; set; }
            }

            public partial class OrderLineStore : Inquiry.Stores.InquiryStore<Demo.OrderLine>
            {
                [InquiryUpdateAll]
                public partial Task<int> UpdateAllAsync(IEnumerable<OrderLine> lines, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OrderLineStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Composite key: both key columns AND-composed in the WHERE, bound as @u{r}_k0 / @u{r}_k1.
        Assert.Contains("WHERE \\\"OrderId\\\" = @u{r}_k0 AND \\\"ProductId\\\" = @u{r}_k1;", text);
        Assert.Contains("_p.ParameterName = \"@u\" + _r + \"_k0\";", text);
        Assert.Contains("_p.ParameterName = \"@u\" + _r + \"_k1\";", text);
    }
}
