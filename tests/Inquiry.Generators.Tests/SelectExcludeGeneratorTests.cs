using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string ExcludeEntities = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TAnnouncement")]
        public sealed class Announcement
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;

            [InquiryColumn("Image")]
            public byte[]? Image { get; set; }

            [InquiryColumn("Body")]
            public string Body { get; set; } = "unread";

            [InquiryColumn("AuthorId")]
            public int AuthorId { get; set; }

            [InquiryColumn("Version")]
            [InquiryConcurrencyToken]
            public int Version { get; set; }

            [InquiryColumn("IsDeleted")]
            [InquirySoftDelete]
            public bool IsDeleted { get; set; }

            [InquiryColumn("IsPublished")]
            [InquiryGlobalFilter]
            public bool IsPublished { get; set; }

            [InquiryRelation(nameof(AuthorId))]
            public Author? Author { get; set; }
        }

        [InquiryTable("TAuthor")]
        public sealed class Author
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        [InquiryProjection(typeof(Announcement))]
        public sealed class AnnouncementTitle
        {
            [InquiryColumn("Title")]
            public string Title { get; set; } = string.Empty;
        }
        """;

    private static string ExcludeStore(string methods) =>
        ExcludeEntities + "\n\npublic partial class AnnouncementStore : Inquiry.Stores.InquiryStore<Demo.Announcement>\n{\n" + methods + "\n}\n";

    private static string GetExcludeStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("AnnouncementStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private static string GetConst(string text, string name)
    {
        var start = text.IndexOf("const string " + name + " = ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"const {name} was not generated");
        var end = text.IndexOf(';', start);
        return text.Substring(start, end - start).Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    [Fact]
    public void SelectAllExclude_OmitsTheColumnAndReadsOnlyTheSelectedOnes()
    {
        var result = RunGenerator(ExcludeStore("""
            [InquirySelectAll(Exclude = new[] { nameof(Announcement.Image) })]
            public partial Task<IReadOnlyList<Announcement>> ListAsync(CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetExcludeStore(result);

        var sql = GetConst(text, "_sqlSelectAll_ListAsync");
        Assert.Contains("\"Id\", \"Title\", \"Body\", \"AuthorId\", \"Version\", \"IsDeleted\", \"IsPublished\" FROM", sql);
        Assert.DoesNotContain("Image", sql);
        // The active-row filter still applies to the column subset.
        Assert.Contains("\"IsDeleted\" = 0", sql);
        Assert.Contains("\"IsPublished\" = 1", sql);
        Assert.DoesNotContain("_sqlSelectAll =", text);

        // The subset materializer reads ordinals 0..6 in order and never sets Image.
        var start = text.IndexOf("private readonly struct __InquiryReadMaterializer_ListAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = text.Substring(start, text.IndexOf("\n    }", start, StringComparison.Ordinal) - start);
        Assert.DoesNotContain("Image", body);
        Assert.Contains("IsPublished = reader.GetBoolean(6)", body);
        Assert.DoesNotContain("(7)", body);
        Assert.Contains("__InquiryReadMaterializer_ListAsync>(", text);
    }

    [Fact]
    public void ExcludeIsSupportedOnEveryNonEagerSelectOperation()
    {
        var result = RunGenerator(ExcludeStore("""
            [InquirySelectOneByKey(Exclude = new[] { "Image" })]
            public partial Task<Announcement?> GetAsync(int id, CancellationToken cancellationToken = default);

            [InquirySelectAllByField("Title", Exclude = new[] { nameof(Announcement.Image), nameof(Announcement.Body) })]
            public partial IAsyncEnumerable<Announcement> ByTitleAsync(string title, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate(Exclude = new[] { nameof(Announcement.Image) })]
            [InquiryWhere("Title")]
            public partial Task<IReadOnlyList<Announcement>> SearchAsync(string title, CancellationToken cancellationToken = default);

            [InquirySelectAll(OrderBy = "Title", Paged = true, Exclude = new[] { nameof(Announcement.Image) })]
            public partial Task<IReadOnlyList<Announcement>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate(OrderBy = "Title", Exclude = new[] { nameof(Announcement.Image) })]
            [InquiryWhere("Title")]
            public partial Task<InquiryPagedResult<Announcement>> PagedSearchAsync(string title, int offset, int limit, CancellationToken cancellationToken = default);

            [InquirySelectOneByKey]
            public partial Task<Announcement?> GetFullAsync(int id, CancellationToken cancellationToken = default);
            """));
        AssertNoErrors(result);
        var text = GetExcludeStore(result);

        Assert.DoesNotContain("Image", GetConst(text, "_sqlSelectByKey_GetAsync"));
        var byTitle = GetConst(text, "_sqlSelectBy_Title_ByTitleAsync");
        Assert.DoesNotContain("Image", byTitle);
        Assert.DoesNotContain("\"Body\"", byTitle);
        Assert.DoesNotContain("Image", GetConst(text, "_sqlPredicate_SearchAsync"));
        Assert.DoesNotContain("Image", GetConst(text, "_sql_PageAsync"));
        Assert.DoesNotContain("Image", GetConst(text, "_sql_PagedSearchAsync"));
        // The method without Exclude keeps the shared full-row SQL and the entity materializer.
        Assert.Contains("\"Image\"", GetConst(text, "_sqlSelectByKey"));
        foreach (var name in new[] { "GetAsync", "ByTitleAsync", "SearchAsync", "PageAsync", "PagedSearchAsync" })
        {
            Assert.Contains("__InquiryReadMaterializer_" + name + ">(", text);
        }
        Assert.DoesNotContain("__InquiryReadMaterializer_GetFullAsync", text);
    }

    [Theory]
    [InlineData("nameof(Announcement.Id)", "key column")]
    [InlineData("nameof(Announcement.Version)", "concurrency token")]
    [InlineData("nameof(Announcement.IsDeleted)", "soft-delete column")]
    [InlineData("nameof(Announcement.IsPublished)", "global-filter column")]
    [InlineData("nameof(Announcement.AuthorId)", "foreign key of a relation")]
    [InlineData("\"Missing\"", "no mapped property or column")]
    [InlineData("\"Image\", \"image\"", "more than once")]
    [InlineData("nameof(Announcement.Title)", "orders by it")]
    public void ExcludeOfAColumnTheReadNeedsReportsINQ122(string excluded, string reason)
    {
        var result = RunGenerator(ExcludeStore($$"""
            [InquirySelectAll(OrderBy = "Title", Exclude = new[] { {{excluded}} })]
            public partial Task<IReadOnlyList<Announcement>> ListAsync(CancellationToken cancellationToken = default);
            """));

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ122");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ExcludeOnAProjectionReadReportsINQ122()
    {
        var result = RunGenerator(ExcludeStore("""
            [InquirySelectAll(Exclude = new[] { "Image" })]
            public partial Task<IReadOnlyList<AnnouncementTitle>> TitlesAsync(CancellationToken cancellationToken = default);
            """));

        Assert.Contains(result.RunResult.Diagnostics, static d => d.Id == "INQ122" && d.Severity == DiagnosticSeverity.Error);
    }

    private const string PhotoSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TAlbum")]
        public sealed class Album
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryRelation(nameof(Photo.AlbumId))]
            public List<Photo> Photos { get; set; } = new();
        }

        [InquiryTable("TPhoto")]
        public sealed class Photo
        {
            [InquiryKey]
            public int Id { get; set; }

            [InquiryColumn("AlbumId")]
            public int AlbumId { get; set; }

            [InquiryColumn("Preview")]
            public byte[]? Image { get; set; }

            [InquiryColumn("Caption")]
            public string Preview { get; set; } = string.Empty;
        }

        public partial class PhotoStore : InquiryStore<Photo>
        {
        """;

    [Fact]
    public void ExcludeByExactPropertyNameWinsOverAnotherPropertysColumnName()
    {
        var result = RunGenerator(PhotoSource + """
                [InquirySelectAll(Exclude = new[] { nameof(Photo.Preview) })]
                public partial Task<IReadOnlyList<Photo>> ListAsync(CancellationToken cancellationToken = default);
            }
            """);
        AssertNoErrors(result);
        var text = result.RunResult.GeneratedTrees
            .Single(static t => t.FilePath.EndsWith("PhotoStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

        var sql = GetConst(text, "_sqlSelectAll_ListAsync");
        Assert.Contains("\"Preview\"", sql);
        Assert.DoesNotContain("\"Caption\"", sql);
        Assert.DoesNotContain("Preview = reader", text);
    }

    [Fact]
    public void ExcludeOfAForeignKeyAnotherEntitysRelationJoinsOnReportsINQ122()
    {
        var result = RunGenerator(PhotoSource + """
                [InquirySelectAll(Exclude = new[] { nameof(Photo.AlbumId) })]
                public partial Task<IReadOnlyList<Photo>> ListAsync(CancellationToken cancellationToken = default);
            }
            """);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ122");
        Assert.Contains("relation on another entity", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ExcludeOfEveryViewColumnReportsINQ122()
    {
        var result = RunGenerator("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryView("VPhoto")]
            public sealed class PhotoRow
            {
                [InquiryColumn("Image")]
                public byte[]? Image { get; set; }
            }

            public partial class PhotoRowStore : InquiryStore<PhotoRow>
            {
                [InquirySelectAll(Exclude = new[] { nameof(PhotoRow.Image) })]
                public partial Task<IReadOnlyList<PhotoRow>> ListAsync(CancellationToken cancellationToken = default);
            }
            """);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ122");
        Assert.Contains("no column would remain", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ExcludeOfARequiredMemberReportsINQ122()
    {
        var result = RunGenerator("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TPhoto")]
            public sealed class Photo
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn("Data")]
                public required byte[] Data { get; set; }
            }

            public partial class PhotoStore : InquiryStore<Photo>
            {
                [InquirySelectAll(Exclude = new[] { nameof(Photo.Data) })]
                public partial Task<IReadOnlyList<Photo>> ListAsync(CancellationToken cancellationToken = default);
            }
            """, languageVersion: LanguageVersion.CSharp11);

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ122");
        Assert.Contains("required member", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
