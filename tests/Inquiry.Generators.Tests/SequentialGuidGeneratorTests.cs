using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Sequential GUID keys: <c>[InquiryKey(SequentialGuid = true)]</c> makes insert/upsert/insert-all
/// assign <c>InquiryGuid.NewVersion7()</c> when the key is unset, leaving supplied keys untouched.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string SequentialGuidSource = """
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
        }

        public partial class DocStore : InquiryStore<Doc>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);

            [InquiryUpsert]
            public partial Task<int> UpsertAsync(Doc doc, CancellationToken cancellationToken = default);

            [InquiryInsertAll]
            public partial Task<int> InsertAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void SequentialGuidKeyAssignsV7OnInsertUpsertAndBatch()
    {
        var result = RunGenerator(SequentialGuidSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Single-entity methods: unset check + assignment before binding.
        Assert.Contains("if (doc.Id == global::System.Guid.Empty)", text);
        Assert.Contains("doc.Id = global::Inquiry.InquiryGuid.NewVersion7();", text);

        // Batch insert: assignment happens exactly once in the chunk binder, with no full-list pre-pass.
        Assert.Contains("if (_it.Id == global::System.Guid.Empty)", text);
        Assert.Contains("_it.Id = global::Inquiry.InquiryGuid.NewVersion7();", text);

        // Exactly three assignment sites: Insert, Upsert, and the batch pre-pass. A regression
        // that drops one injection (e.g. the Upsert case) fails this count even though the
        // Contains assertions above would still match the surviving sites.
        Assert.Equal(3, text.Split("global::Inquiry.InquiryGuid.NewVersion7();").Length - 1);
    }

    [Fact]
    public void SqlServerDialectEmitsSqlServerSequentialFactory()
    {
        var result = RunGenerator(SequentialGuidSource, dialect: "SqlServer");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("global::Inquiry.InquiryGuid.NewSqlServerSequential();", text);
        Assert.DoesNotContain("global::Inquiry.InquiryGuid.NewVersion7();", text);
        Assert.Equal(4, text.Split("global::Inquiry.InquiryGuid.NewSqlServerSequential();").Length - 1);
    }

    [Fact]
    public void PostgreSqlDialectEmitsV7Factory()
    {
        var result = RunGenerator(SequentialGuidSource, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("global::Inquiry.InquiryGuid.NewVersion7();", text);
        Assert.DoesNotContain("global::Inquiry.InquiryGuid.NewSqlServerSequential();", text);
    }

    [Fact]
    public void NullableSequentialGuidKeyChecksNullAndEmpty()
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
                [InquiryKey(SequentialGuid = true)]
                public Guid? Id { get; set; }

                [InquiryColumn]
                public string Title { get; set; } = string.Empty;
            }

            public partial class DocStore : InquiryStore<Doc>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Doc doc, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("if (doc.Id is null || doc.Id == global::System.Guid.Empty)", text);
    }

    [Fact]
    public void SequentialGuidOnNonGuidKeyReportsINQ047()
    {
        const string source = """
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(SequentialGuid = true)]
                public long Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ047");
    }

    [Fact]
    public void SequentialGuidOnGeneratedKeyReportsINQ047()
    {
        const string source = """
            using System;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Doc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true, SequentialGuid = true)]
                public Guid Id { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ047");
    }
}
