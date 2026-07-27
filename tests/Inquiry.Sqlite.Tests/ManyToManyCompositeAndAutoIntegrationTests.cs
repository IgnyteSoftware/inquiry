using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// End-to-end coverage for the two many-to-many extensions in #80 against real SQLite: a related entity
/// with a composite key, and a junction table Inquiry synthesizes rather than the user mapping. Both were
/// built with generator assertions; these prove the SQL runs, returns the right rows, and still costs one
/// round trip. The scenarios live in <see cref="ManyToManyExtensionAssertions"/> so all six provider
/// suites share one seed.
/// </summary>
public sealed class ManyToManyCompositeAndAutoIntegrationTests
{
    private static async Task<(SqliteTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)>
        CreateGridHarnessAsync(string ddl, string name)
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqliteTestHarness.CreateAsync(ddl, name,
            configureServices: s =>
            {
                s.AddSingleton<IInquiryCommandInterceptor>(recorder);
                probe.Decorate(s);
            });
        return (harness, probe, recorder);
    }

    private static Task<SqliteTestHarness> CompositeHarnessAsync()
        => SqliteTestHarness.CreateAsync(FeatureSchema.CompositeManyToManySqliteDdl, "M2MComposite");

    private static Task<SqliteTestHarness> AutoHarnessAsync()
        => SqliteTestHarness.CreateAsync(FeatureSchema.AutoJunctionSqliteDdl, "M2MAuto");

    [Fact]
    public async Task CompositeKeySingleEagerPairsBothKeyComponents()
    {
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.SingleEagerPairsBothKeyComponentsAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [Fact]
    public async Task CompositeKeyAllEagerAssemblesEachPostsTags()
    {
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.AllEagerAssemblesEachPostsTagsAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [Fact]
    public async Task CompositeKeyAllEagerIncludingDeletedKeepsChildFilters()
    {
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.AllEagerIncludingDeletedKeepsChildFiltersAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [Fact]
    public async Task CompositeKeyEagerLoadCostsOneRoundTrip()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            FeatureSchema.CompositeManyToManySqliteDdl, "M2MCompositeGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.CompositeEagerLoadCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task CompositeKeyAllEagerCostsOneRoundTrip()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            FeatureSchema.CompositeManyToManySqliteDdl, "M2MCompositeAllGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.CompositeAllEagerCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task AutoJunctionSingleEagerReadsThroughTheSynthesizedTable()
    {
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionSingleEagerReadsThroughSynthesizedTableAsync(
            harness.GetRequiredService<M2MAuthorStore>());
    }

    [Fact]
    public async Task AutoJunctionAllEagerAssemblesFromBothSides()
    {
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionAllEagerAssemblesFromBothSidesAsync(
            harness.GetRequiredService<M2MAuthorStore>(),
            harness.GetRequiredService<M2MBookStore>());
    }

    [Fact]
    public async Task AutoJunctionAllEagerIncludingDeletedReturnsDeletedParents()
    {
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionAllEagerIncludingDeletedAsync(
            harness.GetRequiredService<M2MAuthorStore>());
    }

    [Fact]
    public async Task AutoJunctionEagerLoadCostsOneRoundTrip()
    {
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            FeatureSchema.AutoJunctionSqliteDdl, "M2MAutoGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.AutoJunctionEagerLoadCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MAuthorStore>(), probe, recorder);
    }

    [Fact]
    public void GeneratedSqlNamesTheSynthesizedJunctionTheDdlCreates()
        => ManyToManyExtensionAssertions.GeneratedSqlNamesTheSynthesizedJunction();
}
