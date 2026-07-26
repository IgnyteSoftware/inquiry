using System.Text.RegularExpressions;
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
        AssertSingleGridCommand(probe, recorder, "Region", "Territories");
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
        AssertSingleGridCommand(probe, recorder, "Region", "Territories");
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
        AssertSingleGridCommand(probe, recorder, "Region", "Territories");
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
        AssertSingleGridCommand(probe, recorder, "Region", "Territories");
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
        params string[] expectedTables)
    {
        var command = Assert.Single(probe.FinalizedCommands);
        Assert.Equal(0, probe.CreateBatchCount);
        Assert.Empty(recorder.Commands);
        foreach (var table in expectedTables)
        {
            AssertSelectsFrom(command.CommandText, table);
        }
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
