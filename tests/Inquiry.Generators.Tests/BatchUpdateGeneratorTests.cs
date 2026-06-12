using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Batch-update emission: <c>[InquiryUpdateAll]</c> reuses the single-row <c>_sqlUpdate</c> const and
/// routes through <c>Inquiry.ExecuteBatchAsync</c> — one UPDATE per item, a single DbBatch round trip
/// where the provider supports it — with a per-item binder mirroring the single-row update binder.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void UpdateAllEmitsBatchExecuteOverSingleRowUpdate()
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

        // Key is generated → excluded from SET, referenced in the WHERE of the shared single-row UPDATE.
        Assert.Contains("private const string _sqlUpdate = \"UPDATE \\\"TThing\\\" SET \\\"Name\\\" = @Name, \\\"Qty\\\" = @Qty WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("return await Inquiry.ExecuteBatchAsync(", text);
        Assert.Contains("_sqlUpdate,", text);
        Assert.Contains("static (_t, _it) =>", text);
        // Binder mirrors the single-row update binder: @PropertyName params written to the target.
        Assert.Contains("var _p0 = _t.CreateParameter();", text);
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_t.AddParameter(_p0);", text);
        Assert.Contains("_p1.ParameterName = \"@Name\";", text);
        Assert.Contains("_p2.ParameterName = \"@Qty\";", text);
        Assert.Contains("_p2.Value = (object?)_it.Qty ?? global::System.DBNull.Value;", text);
        // The old per-row template/segments and parameter-cap guard are gone.
        Assert.DoesNotContain("_sqlUpdateAllRow", text);
        Assert.DoesNotContain("MaxParametersPerCommand", text);
    }

    [Fact]
    public void UpdateAllMaterializesLazyEnumerableWithoutParameterCap()
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

        Assert.DoesNotContain("global::System.Linq.Enumerable.ToList(things)", text);
        Assert.Contains("var _list = things as global::System.Collections.Generic.IReadOnlyList<global::Demo.Thing>;", text);
        Assert.Contains("if (things is null) throw new global::System.ArgumentNullException(nameof(things));", text);
        Assert.Contains("var _tmp = new global::System.Collections.Generic.List<global::Demo.Thing>();", text);
        Assert.Contains("foreach (var _item in things)", text);
        Assert.Contains("_tmp.Add(_item);", text);
        Assert.Contains("if (_list.Count == 0) return 0;", text);
        // Each item binds to its own command in the batch, so no per-command parameter cap applies.
        Assert.DoesNotContain("MaxParametersPerCommand", text);
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

        // Composite key: both key columns AND-composed in the single-row UPDATE's WHERE, and both
        // bound by the batch binder alongside the SET column.
        Assert.Contains("WHERE \\\"OrderId\\\" = @OrderId AND \\\"ProductId\\\" = @ProductId\";", text);
        Assert.Contains("_p0.ParameterName = \"@OrderId\";", text);
        Assert.Contains("_p1.ParameterName = \"@ProductId\";", text);
        Assert.Contains("_p2.ParameterName = \"@Qty\";", text);
    }
}
