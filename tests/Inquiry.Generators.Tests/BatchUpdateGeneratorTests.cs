using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Batch-update emission: <c>[InquiryUpdate]</c> reuses the single-row <c>_sqlUpdate</c> const and
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
                [InquiryUpdate]
                public partial Task<int> UpdateAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Key is generated → excluded from SET, referenced in the WHERE of the shared single-row UPDATE.
        Assert.Contains("private const string _sqlUpdate = \"UPDATE \\\"TThing\\\" SET \\\"Name\\\" = @Name, \\\"Qty\\\" = @Qty WHERE \\\"Id\\\" = @Id\";", text);
        Assert.Contains("private static readonly global::Inquiry.Commands.InquiryBatchCommand<global::Demo.Thing> _batch_UpdateAllAsync_", text);
        Assert.Contains("_sqlUpdate,", text);
        Assert.Contains("static (_t, _it) =>", text);
        // Binder mirrors the single-row update binder: @PropertyName params written to the target.
        Assert.Contains("var _p0 = _t.CreateParameter();", text);
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_t.AddParameter(_p0);", text);
        Assert.Contains("_p1.ParameterName = \"@Name\";", text);
        Assert.Contains("_p2.ParameterName = \"@Qty\";", text);
        Assert.Contains("_p2.Value = (object?)_it.Qty ?? global::System.DBNull.Value;", text);
        Assert.Contains("return Inquiry.ExecuteBatchAsync(_batch_UpdateAllAsync_", text);
        // The old per-call materialization/template/segments and parameter-cap guard are gone.
        Assert.DoesNotContain("var _list =", text);
        Assert.DoesNotContain("_sqlUpdateAllRow", text);
        Assert.DoesNotContain("MaxParametersPerCommand", text);
    }

    [Fact]
    public void UpdateAllPassesLazyEnumerableToBoundedRuntimeWithoutMaterializing()
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
                [InquiryUpdate]
                public partial Task<int> UpdateAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.DoesNotContain("global::System.Linq.Enumerable.ToList(things)", text);
        Assert.DoesNotContain("var _list =", text);
        Assert.DoesNotContain("var _tmp =", text);
        Assert.DoesNotContain("foreach (var _item in things)", text);
        Assert.Contains("return Inquiry.ExecuteBatchAsync(_batch_UpdateAllAsync_", text);
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
                [InquiryUpdate]
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

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void MySqlFamilyUpdateAllUsesSetBasedChunkForUniqueSafeKeys(string dialect)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;

                [InquiryModifiedAt]
                public System.DateTime ModifiedAt { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryUpdate]
                public partial Task<int> UpdateAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("UPDATE `TThing` AS `_t` INNER JOIN (", text);
        Assert.Contains("_sql.Append(_r == 0 ? \"SELECT \" : \" UNION ALL SELECT \");", text);
        Assert.Contains("if (_r == 0) _sql.Append(\" AS `Id`\");", text);
        Assert.Contains(") AS `_v` ON `_t`.`Id` = `_v`.`Id` SET `_t`.`Name` = `_v`.`Name`, `_t`.`ModifiedAt` = `_v`.`ModifiedAt`", text);
        Assert.Contains("new global::System.Collections.Generic.HashSet<long>(_items.Count)", text);
        Assert.Contains("if (!_keys.Add(_items[_i].Id)) return false;", text);
        Assert.Contains("parametersPerItem: 3,", text);
        Assert.Contains("maxItemsPerCommand: 21845);", text);
        Assert.Equal(2, text.Split("_it.ModifiedAt = global::System.DateTime.UtcNow;").Length - 1);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void MySqlFamilyUpdateAllKeepsFixedRowFallbackForCollatedKeys(string dialect)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(Length = 64)]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn]
                public int Qty { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryUpdate]
                public partial Task<int> UpdateAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("_sqlUpdate,", text);
        Assert.DoesNotContain("INNER JOIN (", text);
        Assert.DoesNotContain("HashSet<", text);
        Assert.DoesNotContain("parametersPerItem:", text);
    }

    private static string BatchDescriptor(string generated, string methodName)
    {
        var name = generated.IndexOf("_batch_" + methodName, StringComparison.Ordinal);
        Assert.True(name >= 0, $"Generated batch descriptor for {methodName} was not found.");
        var start = generated.LastIndexOf('\n', name) + 1;
        var crlfEnd = generated.IndexOf("\r\n\r\n", name, StringComparison.Ordinal);
        var lfEnd = generated.IndexOf("\n\n", name, StringComparison.Ordinal);
        var end = crlfEnd >= 0 ? crlfEnd : lfEnd;
        Assert.True(end > name, $"Generated batch descriptor for {methodName} had no terminator.");
        return generated[start..end];
    }
}
