using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W3 batch-insert emission: <c>[InquiryInsertAll]</c> emits a prefix const plus a runtime-built
/// multi-row VALUES clause, bound through the existing ExecuteAsync&lt;TArgs&gt; fast path.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void InsertAllEmitsMultiRowBatch()
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsertAllPrefix = \"INSERT INTO \\\"TThing\\\" (\\\"Id\\\", \\\"Name\\\") VALUES \";", text);
        Assert.Contains("Inquiry.ExecuteAsync<global::System.Collections.Generic.IReadOnlyList<global::Demo.Thing>>(", text);
        // Per-row placeholders and matching bound parameter names.
        Assert.Contains("_sb.Append(\"(@p\").Append(_r).Append(\"_0\");", text);
        Assert.Contains("_p.ParameterName = \"@p\" + _r + \"_1\";", text);
        Assert.Contains("if (_list.Count == 0) return 0;", text);
    }
}
