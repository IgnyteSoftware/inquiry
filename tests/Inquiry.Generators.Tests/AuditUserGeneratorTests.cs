using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Auditing user columns: <c>[InquiryCreatedBy]</c> is stamped from <c>InquiryAuditContext.CurrentUser</c>
/// on insert when unset and excluded from every UPDATE SET; <c>[InquiryModifiedBy]</c> is stamped on
/// every generated insert/update/upsert. Misuse is INQ055/INQ056.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void AuditUserStampsInsertAndExcludesCreatedByFromUpdate()
    {
        const string source = """
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

                [InquiryCreatedBy]
                public string? CreatedBy { get; set; }

                [InquiryModifiedBy]
                public string? ModifiedBy { get; set; }
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);

                [InquiryUpdate]
                public partial Task<bool> UpdateAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var text = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        // Insert: CreatedBy stamped only when unset (null/empty); ModifiedBy stamped unconditionally.
        Assert.Contains("if (global::System.String.IsNullOrEmpty(doc.CreatedBy))", text);
        Assert.Contains("doc.CreatedBy = global::Inquiry.InquiryAuditContext.CurrentUser;", text);
        Assert.Contains("doc.ModifiedBy = global::Inquiry.InquiryAuditContext.CurrentUser;", text);

        // The UPDATE SET excludes CreatedBy and never binds it; ModifiedBy is set.
        Assert.Contains("_sqlUpdate = \"UPDATE \\\"Doc\\\" SET \\\"Title\\\" = @Title, \\\"ModifiedBy\\\" = @ModifiedBy WHERE \\\"Id\\\" = @Id\";", text);
        // Insert SQL still writes CreatedBy.
        Assert.Contains("INSERT INTO \\\"Doc\\\" (\\\"Title\\\", \\\"CreatedBy\\\", \\\"ModifiedBy\\\")", text);
    }

    [Fact]
    public void NonStringAuditUserColumnReportsINQ055()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryCreatedBy]
                public int CreatedBy { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ055");
    }

    [Fact]
    public void DuplicateModifiedByReportsINQ056()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryModifiedBy]
                public string? A { get; set; }

                [InquiryModifiedBy]
                public string? B { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ056");
    }
}
