using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryBulkInsert]</c>: on bulk-copy dialects (SqlServer/PostgreSql/MySql) the method emits
/// a static <c>InquiryBulkInsertDefinition</c> + a call into <c>IInquiry.BulkInsertAsync</c>; on
/// dialects without a native bulk-copy API it compiles down to the multi-row batch-insert body.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string BulkInsertSource = """
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
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Cat")]
            public string Category { get; set; } = string.Empty;

            [InquiryColumn]
            public decimal Amount { get; set; }

            [InquiryColumn]
            public string? Note { get; set; }
        }

        public partial class ItemStore : InquiryStore<Item>
        {
            [InquiryBulkInsert]
            public partial Task<long> BulkInsertAsync(IEnumerable<Item> items, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void BulkInsertOnPostgreSqlEmitsDefinitionAndCopierCall()
    {
        var result = RunGenerator(BulkInsertSource, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Static definition: raw table + raw column names (generated key excluded), ordinal accessor.
        Assert.Contains("private static readonly global::Inquiry.BulkCopy.InquiryBulkInsertDefinition<global::Demo.Item> _bulkDef_BulkInsertAsync = new(", text);
        Assert.Contains("new[] { \"Cat\", \"Amount\", \"Note\" },", text);
        Assert.Contains("0 => (object?)_e.Category ?? global::System.DBNull.Value,", text);
        Assert.Contains("2 => (object?)_e.Note ?? global::System.DBNull.Value,", text);
        Assert.Contains("new global::System.Type[] {", text);
        Assert.Contains("typeof(string)", text);
        Assert.Contains("typeof(decimal)", text);

        // The body streams through the copier; no batch-SQL machinery is emitted.
        Assert.Contains("return Inquiry.BulkInsertAsync(_bulkDef_BulkInsertAsync, items,", text);
        Assert.DoesNotContain("_sqlInsertAllPrefix", text);
    }

    [Fact]
    public void BulkInsertOnSqliteFallsBackToBatchInsertBody()
    {
        var result = RunGenerator(BulkInsertSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Compile-time fallback: the batch-insert consts + ExecuteAsync body, no copier call.
        Assert.Contains("_sqlInsertAllPrefix", text);
        Assert.Contains("Inquiry.ExecuteAsync", text);
        Assert.DoesNotContain("InquiryBulkInsertDefinition", text);
        Assert.DoesNotContain("Inquiry.BulkInsertAsync(", text);
    }

    [Fact]
    public void BulkInsertStampsSequentialGuidAndAuditColumnsPerRow()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(SequentialGuid = true)]
                public Guid Id { get; set; }

                [InquiryColumn]
                public string Title { get; set; } = string.Empty;

                [InquiryCreatedAt]
                public DateTime CreatedAt { get; set; }

                [InquiryModifiedAt]
                public DateTime ModifiedAt { get; set; }
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryBulkInsert]
                public partial Task<long> BulkInsertAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Streaming stamp iterator wraps the source; rows are stamped as they are enumerated.
        Assert.Contains("_Stamped(docs)", text);
        Assert.Contains("_e.Id = global::Inquiry.InquiryGuid.NewVersion7();", text);
        Assert.Contains("if (_e.CreatedAt == default)", text);
        Assert.Contains("_e.ModifiedAt = global::System.DateTime.UtcNow;", text);
        Assert.Contains("yield return _e;", text);
    }

    [Fact]
    public void BulkInsertWithIntReturnReportsUnsupportedReturnType()
    {
        const string source = """
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
            }

            public partial class ItemStore : InquiryStore<Item>
            {
                [InquiryBulkInsert]
                public partial Task<int> BulkInsertAsync(IEnumerable<Item> items, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ005");
    }
}
