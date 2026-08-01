using System.Text.RegularExpressions;
using Inquiry.FeatureCatalog;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Testing;

namespace Inquiry.Tests.Shared;

/// <summary>
/// Shared seed + act + assert for the eager-grid round-trip tests (#70). Each provider suite owns harness
/// construction — harness types, DDL constants, and Docker gating all differ — but the assertions live here
/// so the six copies cannot drift apart again.
/// </summary>
/// <remarks>
/// The earlier assertion was <c>Assert.Empty(recorder.Commands)</c> alone, which proves nothing about the
/// round-trip count: <c>QueryMultipleAsync</c> never invokes <see cref="IInquiryCommandInterceptor"/>, so an
/// empty recorder is satisfied by one grid command, by N grid commands, and by no query at all.
/// </remarks>
internal static class EagerGridCommandAssertions
{
    internal static async Task SelectOneByKeyEagerIssuesOneCommandAsync(
        RegionStore regions,
        TerritoryStore territories,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedAsync(regions, territories, probe, recorder);

        var loaded = await regions.SelectByKeyWithTerritoriesAsync(1);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Territories!);
        AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, "Region", "Territories");
    }

    internal static async Task SelectAllEagerIssuesOneCommandAsync(
        RegionStore regions,
        TerritoryStore territories,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedAsync(regions, territories, probe, recorder);

        var all = await ToListAsync(regions.SelectAllWithTerritoriesAsync());

        Assert.Single(all);
        Assert.Single(all[0].Territories!);
        AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, "Region", "Territories");
    }

    internal static async Task SelectOneByKeyEagerWithReferenceIssuesOneCommandAsync(
        RegionStore regions,
        TerritoryStore territories,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedAsync(regions, territories, probe, recorder);

        var loaded = await territories.SelectByKeyWithRegionAsync("T1");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Region);
        Assert.Equal("Eastern", loaded.Region!.RegionDescription);
        AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, "Region", "Territories");
    }

    internal static async Task SelectAllEagerWithReferenceIssuesOneCommandAsync(
        RegionStore regions,
        TerritoryStore territories,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedAsync(regions, territories, probe, recorder);

        var all = await ToListAsync(territories.SelectAllWithRegionAsync());

        Assert.Single(all);
        Assert.NotNull(all[0].Region);
        AssertSingleGridCommand(probe, recorder, expectedResultSets: 2, "Region", "Territories");
    }

    // ---- 1 parent + 2 relations (#70). EagerMixedPost has both a to-one Author and a to-many Tags,
    // so one eager load must still batch all three SELECTs into a single command. ----

    internal static async Task SelectOneByKeyEagerWithTwoRelationsIssuesOneCommandAsync(
        EagerMixedAuthorStore authors,
        EagerMixedPostStore posts,
        EagerMixedTagStore tags,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedMixedAsync(authors, posts, tags, probe, recorder);

        var loaded = await posts.GetWithAuthorAndTagsAsync(10);

        Assert.NotNull(loaded);
        Assert.Equal("First", loaded!.Title);
        Assert.NotNull(loaded.Author);
        Assert.Equal("Ada", loaded.Author!.Name);
        Assert.Equal(
            new[] { "alpha", "beta" },
            loaded.Tags.Select(t => t.Label).OrderBy(s => s, StringComparer.Ordinal));

        AssertSingleGridCommand(probe, recorder, expectedResultSets: 3, "EagerMixedPost", "EagerMixedAuthor", "EagerMixedTag");
    }

    internal static async Task SelectAllEagerWithTwoRelationsIssuesOneCommandAsync(
        EagerMixedAuthorStore authors,
        EagerMixedPostStore posts,
        EagerMixedTagStore tags,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await SeedMixedAsync(authors, posts, tags, probe, recorder);

        var all = await ToListAsync(posts.SelectAllWithAuthorAndTagsAsync());

        Assert.Equal(2, all.Count);

        var first = all.Single(p => p.Id == 10);
        Assert.Equal("Ada", first.Author!.Name);
        Assert.Equal(
            new[] { "alpha", "beta" },
            first.Tags.Select(t => t.Label).OrderBy(s => s, StringComparer.Ordinal));

        // Second post resolves a different author and has no tags — the empty-collection branch.
        var second = all.Single(p => p.Id == 11);
        Assert.Equal("Grace", second.Author!.Name);
        Assert.Empty(second.Tags);

        AssertSingleGridCommand(probe, recorder, expectedResultSets: 3, "EagerMixedPost", "EagerMixedAuthor", "EagerMixedTag");
    }

    private static async Task SeedMixedAsync(
        EagerMixedAuthorStore authors,
        EagerMixedPostStore posts,
        EagerMixedTagStore tags,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await authors.InsertAsync(new EagerMixedAuthor { Id = 1, Name = "Ada" });
        await authors.InsertAsync(new EagerMixedAuthor { Id = 2, Name = "Grace" });
        await posts.InsertAsync(new EagerMixedPost { Id = 10, AuthorId = 1, Title = "First" });
        await posts.InsertAsync(new EagerMixedPost { Id = 11, AuthorId = 2, Title = "Second" });
        await tags.InsertAsync(new EagerMixedTag { Id = 100, PostId = 10, Label = "alpha" });
        await tags.InsertAsync(new EagerMixedTag { Id = 101, PostId = 10, Label = "beta" });

        recorder.Clear();
        probe.Reset();
    }

    // Each provider suite has its own ToListAsync extension in its own Fixtures namespace, so this shared
    // file cannot use any of them. Enumerate locally instead.
    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static async Task SeedAsync(
        RegionStore regions,
        TerritoryStore territories,
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder)
    {
        await regions.InsertAsync(new Region { RegionID = 1, RegionDescription = "Eastern" });
        await territories.InsertAsync(new Territory { TerritoryID = "T1", TerritoryDescription = "Boston", RegionID = 1 });

        // The seed inserts finalize commands of their own; both signals must start from a clean slate.
        recorder.Clear();
        probe.Reset();
    }

    /// <summary>
    /// Asserts the operation cost exactly one round trip and that it went through the grid path.
    /// </summary>
    /// <remarks>
    /// Three signals, none sufficient alone:
    /// <list type="bullet">
    ///   <item>
    ///     Exactly one finalized command. Every pipeline command-execution path calls
    ///     <c>IInquiryConnectionFactory.FinalizeCommand</c> exactly once per <c>DbCommand</c> — the grid
    ///     path and the generated paths do so directly, boxed commands via <c>InitializeCommandSync</c> —
    ///     so this fails at N (a relation-per-query regression) and at 0 alike.
    ///   </item>
    ///   <item>
    ///     No <c>DbBatch</c> was created. <c>FinalizeCommand</c> is deliberately not called on the
    ///     <c>DbBatch</c> path (see <c>IInquiryConnectionFactory</c>), so batch round trips are invisible
    ///     to the command count; asserting the batch count closes that blind spot.
    ///   </item>
    ///   <item>
    ///     An empty recorder proves it was <c>QueryMultipleAsync</c>, the only path that bypasses
    ///     <see cref="IInquiryCommandInterceptor"/>. Without this, a single-command non-grid
    ///     implementation would pass unnoticed.
    ///   </item>
    /// </list>
    /// </remarks>
    internal static void AssertSingleGridCommand(
        BatchExecutionProbe probe,
        RecordingCommandInterceptor recorder,
        int expectedResultSets,
        params string[] expectedTables)
    {
        var command = Assert.Single(probe.FinalizedCommands);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Empty(recorder.Commands);
        AssertResultSetCount(command.CommandText, expectedResultSets);
        foreach (var table in expectedTables)
        {
            AssertSelectsFrom(command.CommandText, table);
        }
    }

    /// <summary>
    /// Asserts the single command really carries <paramref name="expected"/> result sets.
    /// </summary>
    /// <remarks>
    /// This is the assertion that actually pins "one command, N result sets". Table-name checks cannot:
    /// the relation SELECTs filter through a parent-key subquery, so <c>FROM EagerMixedPost</c> appears
    /// inside the child statements too, and a batch that lost the parent SELECT entirely would still
    /// match every expected table name.
    /// Generated SQL contains no string literals, so counting separators is exact. Oracle multiplexes
    /// implicit result sets through <c>DBMS_SQL.RETURN_RESULT</c>; every other dialect emits a
    /// <c>;</c>-separated batch with no trailing separator.
    /// </remarks>
    private static void AssertResultSetCount(string commandText, int expected)
    {
        const string OracleMarker = "DBMS_SQL.RETURN_RESULT";
        var actual = commandText.Contains(OracleMarker, StringComparison.OrdinalIgnoreCase)
            ? Regex.Matches(commandText, Regex.Escape(OracleMarker), RegexOptions.IgnoreCase).Count
            : commandText.Split(';').Length;

        Assert.True(
            actual == expected,
            $"Expected {expected} result sets in the single grid command but counted {actual}:{Environment.NewLine}{commandText}");
    }

    /// <summary>
    /// Asserts the command selects from <paramref name="table"/>, anchored on the FROM clause.
    /// </summary>
    /// <remarks>
    /// A bare <c>Contains("Region")</c> is vacuous here: every <c>Territories</c> SELECT carries a
    /// <c>RegionID</c> column, so in the reference direction both table names matched even when the batch
    /// held a single result set. Anchoring on <c>FROM</c> and requiring a word boundary after the name
    /// makes the check prove what it claims — that this one command really does carry both SELECTs.
    /// The optional leading delimiter covers every dialect's quoting: <c>[Territories]</c>,
    /// <c>"Territories"</c>, <c>`Territories`</c>, and Oracle's bare identifier.
    /// </remarks>
    private static void AssertSelectsFrom(string commandText, string table)
    {
        var pattern = @"FROM\s+[""\[`]?" + Regex.Escape(table) + @"\b";
        Assert.True(
            Regex.IsMatch(commandText, pattern, RegexOptions.IgnoreCase),
            $"Expected a SELECT ... FROM {table} in the single grid command, but got:{Environment.NewLine}{commandText}");
    }
}
