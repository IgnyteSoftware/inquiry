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
        // Per-row open is a separate const ("(" for multi-row VALUES; Oracle overrides it for INSERT ALL).
        Assert.Contains("private const string _sqlInsertAllRowOpen = \"(\";", text);
        Assert.Contains("Inquiry.ExecuteAsync<global::System.Collections.Generic.IReadOnlyList<global::Demo.Thing>>(", text);
        // Per-row placeholders and matching bound parameter names.
        Assert.Contains("_sb.Append(\"@p\").Append(_r).Append(\"_0\");", text);
        Assert.Contains("_p.ParameterName = \"@p\" + _r + \"_1\";", text);
        Assert.Contains("if (_list.Count == 0) return 0;", text);
    }

    [Fact]
    public void InsertAllOmitsDatabaseGeneratedTokenColumn()
    {
        // A SQL Server rowversion (DatabaseGenerated token) is supplied by the DB — it must be absent
        // from both the prefix column list and the bound values, matching the single-row insert.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDoc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("RowVer"), InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] RowVer { get; set; } = Array.Empty<byte>();
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsertAllPrefix = \"INSERT INTO [TDoc] ([Id], [Name]) VALUES \";", text);
        Assert.DoesNotContain("RowVer", text);
    }
}
