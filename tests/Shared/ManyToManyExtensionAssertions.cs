using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Testing;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Shared seed + act + assert for the two many-to-many extensions in #80: a related entity with a
/// composite key, and a junction table Inquiry synthesizes rather than the user mapping. Each provider
/// suite owns harness construction — harness types, DDL constants, and Docker gating all differ — but
/// the assertions live here, for the same reason <see cref="EagerGridCommandAssertions"/> does: six
/// copies of a seed drift, and a scenario edited in five places out of six leaves a suite that looks
/// green while testing something different per provider.
/// </summary>
internal static class ManyToManyExtensionAssertions
{
    /// <summary>
    /// Seeds tags whose key components each recur under more than one value of the other, then links
    /// post1 to exactly one of them. This is what makes the assertions discriminating: matching on
    /// <c>Slug</c> alone would additionally return tag (2, "red"), and matching on <c>TenantId</c> alone
    /// would additionally return (1, "blue"). Only a join pairing BOTH components returns the one tag.
    /// </summary>
    internal static async Task<(long Post1, long Post2, long DeletedPost, long UnlinkedPost)> SeedCompositeAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links)
    {
        var post1 = (await posts.InsertAsync(new M2MPost { Title = "First" }))!.Id;
        var post2 = (await posts.InsertAsync(new M2MPost { Title = "Second" }))!.Id;
        var deletedPost = (await posts.InsertAsync(new M2MPost { Title = "Gone", IsDeleted = true }))!.Id;

        // Never linked to anything: the grouping must leave an initialized empty collection, not null
        // and not the previous parent's list. The tuple-keyed grouping a composite child uses is a
        // different code path from the scalar-keyed one, so it needs its own miss case.
        var unlinkedPost = (await posts.InsertAsync(new M2MPost { Title = "Untagged" }))!.Id;

        await tags.InsertAsync(new M2MTag { TenantId = 1, Slug = "red", Label = "Red T1" });
        await tags.InsertAsync(new M2MTag { TenantId = 1, Slug = "blue", Label = "Blue T1" });
        await tags.InsertAsync(new M2MTag { TenantId = 2, Slug = "red", Label = "Red T2" });
        await tags.InsertAsync(new M2MTag { TenantId = 2, Slug = "gone", Label = "Deleted", IsDeleted = true });

        await links.LinkAsync(new M2MPostTag { PostId = post1, TenantId = 1, Slug = "red" });
        await links.LinkAsync(new M2MPostTag { PostId = post2, TenantId = 1, Slug = "blue" });
        await links.LinkAsync(new M2MPostTag { PostId = post2, TenantId = 2, Slug = "red" });
        await links.LinkAsync(new M2MPostTag { PostId = deletedPost, TenantId = 1, Slug = "blue" });

        // Excluded two ways: the tag is soft-deleted, and so is the link itself.
        await links.LinkAsync(new M2MPostTag { PostId = post1, TenantId = 2, Slug = "gone" });
        await links.LinkAsync(new M2MPostTag { PostId = post1, TenantId = 2, Slug = "red", IsDeleted = true });

        return (post1, post2, deletedPost, unlinkedPost);
    }

    internal static async Task SingleEagerPairsBothKeyComponentsAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links)
    {
        var (post1, _, _, _) = await SeedCompositeAsync(posts, tags, links);

        var loaded = await posts.GetWithTagsAsync(post1);

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "Red T1" }, Labels(loaded!.Tags));
    }

    internal static async Task AllEagerAssemblesEachPostsTagsAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links)
    {
        var (post1, post2, deletedPost, unlinkedPost) = await SeedCompositeAsync(posts, tags, links);

        var all = await ToListAsync(posts.AllWithTagsAsync());

        Assert.Equal(new[] { post1, post2, unlinkedPost }.OrderBy(id => id), all.Select(p => p.Id).OrderBy(id => id));
        Assert.DoesNotContain(all, p => p.Id == deletedPost);
        Assert.Equal(new[] { "Red T1" }, Labels(all.Single(p => p.Id == post1).Tags));
        Assert.Equal(new[] { "Blue T1", "Red T2" }, Labels(all.Single(p => p.Id == post2).Tags));
        Assert.Empty(all.Single(p => p.Id == unlinkedPost).Tags);
    }

    /// <summary>
    /// The composite all-eager path is the one that emits the correlated-EXISTS junction SELECT. Its
    /// single-parent counterpart does not, so asserting the round trip only there would leave the
    /// three-result-set batch this feature actually added unmeasured.
    /// </summary>
    internal static async Task CompositeAllEagerCostsOneRoundTripAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links,
        BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
    {
        await SeedCompositeAsync(posts, tags, links);

        probe.Reset();
        recorder.Clear();
        var all = await ToListAsync(posts.AllWithTagsAsync());

        Assert.Equal(3, all.Count);
        EagerGridCommandAssertions.AssertSingleGridCommand(
            probe, recorder, expectedResultSets: 3, "M2MPost", "M2MTag", "M2MPostTag");
    }

    /// <summary>
    /// The IncludeDeleted variant builds its child and junction SELECTs from a different parent context,
    /// so for a composite child it exercises the correlated-EXISTS branch a second time. Nothing else
    /// compiles or runs those two consts for this shape.
    /// </summary>
    internal static async Task AllEagerIncludingDeletedKeepsChildFiltersAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links)
    {
        var (post1, post2, deletedPost, _) = await SeedCompositeAsync(posts, tags, links);

        var all = await ToListAsync(posts.AllIncludingDeletedWithTagsAsync());

        // The soft-deleted PARENT is now included — that is the point of IncludeDeleted — while the
        // child's and the junction's own filters still apply. Asserting the exact set, not just that the
        // deleted one appears: a variant that narrowed the parent filter the other way would still
        // contain it while silently dropping a live post.
        Assert.Contains(all, p => p.Id == deletedPost);
        Assert.Contains(all, p => p.Id == post2);
        Assert.Equal(4, all.Count);
        Assert.Equal(new[] { "Blue T1" }, Labels(all.Single(p => p.Id == deletedPost).Tags));
        Assert.Equal(new[] { "Red T1" }, Labels(all.Single(p => p.Id == post1).Tags));
    }

    internal static async Task CompositeEagerLoadCostsOneRoundTripAsync(
        M2MPostStore posts, M2MTagStore tags, M2MPostTagStore links,
        BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
    {
        var (post1, _, _, _) = await SeedCompositeAsync(posts, tags, links);

        // Seeding issued its own commands; the assertion is about the eager load alone.
        probe.Reset();
        recorder.Clear();
        var loaded = await posts.GetWithTagsAsync(post1);

        Assert.NotNull(loaded);
        EagerGridCommandAssertions.AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, "M2MPost", "M2MTag");
    }

    internal static async Task AutoJunctionSingleEagerReadsThroughSynthesizedTableAsync(M2MAuthorStore authors)
    {
        var loaded = await authors.GetWithBooksAsync(1);

        Assert.NotNull(loaded);
        // Book 30 is soft-deleted. The junction has no soft-delete column of its own, so the child's own
        // active-row filter is the only thing excluding that link.
        Assert.Equal(new[] { "Analytical", "Compilers" }, Titles(loaded!.Books));
    }

    internal static async Task AutoJunctionAllEagerAssemblesFromBothSidesAsync(
        M2MAuthorStore authors, M2MBookStore books)
    {
        var loadedAuthors = await ToListAsync(authors.AllWithBooksAsync());

        // Author 3 is soft-deleted and never returned; its link to book 10 must not leak either.
        // Author 4 has no links at all — the grouping must leave an empty collection, not null.
        Assert.Equal(new[] { 1L, 2L, 4L }, loadedAuthors.Select(a => a.Id).OrderBy(id => id).ToArray());
        Assert.Equal(new[] { "Analytical", "Compilers" }, Titles(loadedAuthors.Single(a => a.Id == 1).Books));
        Assert.Equal(new[] { "Compilers" }, Titles(loadedAuthors.Single(a => a.Id == 2).Books));
        Assert.Empty(loadedAuthors.Single(a => a.Id == 4).Books);

        // The reverse navigation reads the SAME synthesized table. Both sides declaring is what proves
        // the naming is order-independent: a parent-first derivation would have produced two tables, and
        // this side would query one the DDL never created.
        var loadedBooks = await ToListAsync(books.AllWithAuthorsAsync());

        Assert.Equal(new[] { 10L, 20L }, loadedBooks.Select(b => b.Id).OrderBy(id => id).ToArray());
        Assert.Equal(new[] { "Ada" }, Names(loadedBooks.Single(b => b.Id == 10).Authors));
        Assert.Equal(new[] { "Ada", "Grace" }, Names(loadedBooks.Single(b => b.Id == 20).Authors));
    }

    internal static async Task AutoJunctionAllEagerIncludingDeletedAsync(M2MAuthorStore authors)
    {
        var all = await ToListAsync(authors.AllIncludingDeletedWithBooksAsync());

        Assert.Equal(new[] { 1L, 2L, 3L, 4L }, all.Select(a => a.Id).OrderBy(id => id).ToArray());

        // Author 3's only link is to book 10, which is not deleted — the child filter is unchanged by
        // IncludeDeleted, so book 30 stays out of author 1's collection.
        Assert.Equal(new[] { "Analytical" }, Titles(all.Single(a => a.Id == 3).Books));
        Assert.Equal(new[] { "Analytical", "Compilers" }, Titles(all.Single(a => a.Id == 1).Books));
    }

    internal static async Task AutoJunctionEagerLoadCostsOneRoundTripAsync(
        M2MAuthorStore authors, BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
    {
        var all = await ToListAsync(authors.AllWithBooksAsync());

        Assert.Equal(3, all.Count);
        EagerGridCommandAssertions.AssertSingleGridCommand(
            probe, recorder, expectedResultSets: 3, "M2MAuthor", "M2MBook", "M2MAuthor_M2MBook");
    }

    /// <summary>
    /// Ties the hand-written junction DDL to the names the synthesizer derives. BOTH sides are checked:
    /// <c>M2MAuthor</c> sorts first ordinally, so the author side's const is identical under an
    /// order-independent derivation and a parent-first one — only the book side distinguishes them.
    /// </summary>
    internal static void GeneratedSqlNamesTheSynthesizedJunction()
    {
        foreach (var (storeType, field) in new[]
        {
            (typeof(M2MAuthorStore), "_sql_Books_Junction"),
            (typeof(M2MBookStore), "_sql_Authors_Junction"),
        })
        {
            var sql = Assert.IsType<string>(
                storeType.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue());

            Assert.Contains("M2MAuthor_M2MBook", sql);
            Assert.DoesNotContain("M2MBook_M2MAuthor", sql);
            Assert.Contains("M2MAuthor_Id", sql);
            Assert.Contains("M2MBook_Id", sql);
        }
    }

    private static string[] Labels(IEnumerable<M2MTag> tags) => tags.Select(t => t.Label).OrderBy(l => l).ToArray();

    private static string[] Titles(IEnumerable<M2MBook> books) => books.Select(b => b.Title).OrderBy(t => t).ToArray();

    private static string[] Names(IEnumerable<M2MAuthor> authors) => authors.Select(a => a.Name).OrderBy(n => n).ToArray();

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source) items.Add(item);
        return items;
    }
}
