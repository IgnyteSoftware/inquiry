using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Auditing timestamps: <c>[InquiryCreatedAt]</c> is stamped on insert when unset and excluded from
/// every UPDATE SET + update bind; <c>[InquiryModifiedAt]</c> is stamped on every generated
/// insert/update/upsert (including batch forms) before binding.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string AuditedSource = """
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
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn]
            public string Title { get; set; } = string.Empty;

            [InquiryCreatedAt]
            public DateTime CreatedAt { get; set; }

            [InquiryModifiedAt]
            public DateTime ModifiedAt { get; set; }
        }

        public partial class DocStore : InquiryStore<Doc>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);

            [InquiryUpdate]
            public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);

            [InquiryUpdateAll]
            public partial Task<int> UpdateAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void AuditedEntityStampsInsertAndExcludesCreatedAtFromUpdate()
    {
        var result = RunGenerator(AuditedSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Insert: CreatedAt stamped only when unset; ModifiedAt stamped unconditionally.
        Assert.Contains("if (doc.CreatedAt == default)", text);
        Assert.Contains("doc.CreatedAt = global::System.DateTime.UtcNow;", text);
        Assert.Contains("doc.ModifiedAt = global::System.DateTime.UtcNow;", text);

        // The UPDATE SET must not touch CreatedAt — and the update binder must not bind it
        // (an unreferenced parameter is rejected by some providers).
        Assert.Contains("_sqlUpdate = \"UPDATE \\\"Doc\\\" SET \\\"Title\\\" = @Title, \\\"ModifiedAt\\\" = @ModifiedAt WHERE \\\"Id\\\" = @Id\";", text);

        // Insert SQL still writes CreatedAt.
        Assert.Contains("_sqlInsert = \"INSERT INTO \\\"Doc\\\" (\\\"Title\\\", \\\"CreatedAt\\\", \\\"ModifiedAt\\\") VALUES (@Title, @CreatedAt, @ModifiedAt)\";", text);

        // Batch update: per-item ModifiedAt pre-pass, no CreatedAt stamp on the update path.
        Assert.Contains("_list[_a].ModifiedAt = global::System.DateTime.UtcNow;", text);
        Assert.DoesNotContain("_list[_a].CreatedAt", text);
    }

    [Fact]
    public void AuditedUpsertExcludesCreatedAtFromConflictBranchOnly()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn]
                public string Title { get; set; } = string.Empty;

                [InquiryCreatedAt]
                public DateTimeOffset? CreatedAt { get; set; }

                [InquiryModifiedAt]
                public DateTimeOffset? ModifiedAt { get; set; }
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Nullable DateTimeOffset: null-check + DateTimeOffset.UtcNow.
        Assert.Contains("if (doc.CreatedAt is null)", text);
        Assert.Contains("doc.CreatedAt = global::System.DateTimeOffset.UtcNow;", text);
        Assert.Contains("doc.ModifiedAt = global::System.DateTimeOffset.UtcNow;", text);

        // Insert half writes CreatedAt; the conflict UPDATE SET excludes it.
        Assert.Contains("INSERT INTO \\\"Doc\\\" (\\\"Id\\\", \\\"Title\\\", \\\"CreatedAt\\\", \\\"ModifiedAt\\\")", text);
        Assert.Contains("DO UPDATE SET \\\"Title\\\" = @Title, \\\"ModifiedAt\\\" = @ModifiedAt\";", text);
    }

    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void MySqlAuditedUpsertExcludesCreatedAtFromOnDuplicateKeyBranch(string dialect)
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(Length = 40)]
                public string Id { get; set; } = string.Empty;

                [InquiryColumn]
                public string Title { get; set; } = string.Empty;

                [InquiryCreatedAt]
                public DateTime CreatedAt { get; set; }

                [InquiryModifiedAt]
                public DateTime ModifiedAt { get; set; }
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // MySQL/MariaDb conflict branch uses VALUES(col) assignments built by its own enumerator —
        // CreatedAt must be excluded there too, while the insert list still writes it.
        Assert.Contains("ON DUPLICATE KEY UPDATE `Title` = VALUES(`Title`), `ModifiedAt` = VALUES(`ModifiedAt`)", text);
        Assert.Contains("INSERT INTO `Doc` (`Id`, `Title`, `CreatedAt`, `ModifiedAt`)", text);
    }

    [Fact]
    public void NonTimestampAuditColumnReportsINQ049()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryCreatedAt]
                public string CreatedBy { get; set; } = string.Empty;
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ049");
    }

    [Fact]
    public void DuplicateModifiedAtReportsINQ050()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryModifiedAt]
                public DateTime A { get; set; }

                [InquiryModifiedAt]
                public DateTime B { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ050");
    }
}
