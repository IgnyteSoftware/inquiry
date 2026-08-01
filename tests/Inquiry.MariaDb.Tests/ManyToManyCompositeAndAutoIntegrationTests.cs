using System.Threading.Tasks;
using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// End-to-end coverage for the two many-to-many extensions in #80 against real MariaDb: a related entity
/// with a composite key, and a junction table Inquiry synthesizes rather than the user mapping. Both were
/// built with generator assertions; these prove the SQL runs, returns the right rows, and still costs one
/// round trip. The scenarios live in <see cref="ManyToManyExtensionAssertions"/> so all six provider
/// suites share one seed.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class ManyToManyCompositeAndAutoIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public ManyToManyCompositeAndAutoIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;
    private static async Task<(MariaDbTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)>
        CreateGridHarnessAsync(string adminConnectionString, string ddl, string name)
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(adminConnectionString, ddl, name,
            configureServices: s =>
            {
                s.AddSingleton<IInquiryCommandInterceptor>(recorder);
                probe.Decorate(s);
            });
        return (harness, probe, recorder);
    }

    private Task<MariaDbTestHarness> CompositeHarnessAsync()
        => MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.CompositeManyToManyMySqlDdl, "M2MComposite");

    private Task<MariaDbTestHarness> AutoHarnessAsync()
        => MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AutoJunctionMySqlDdl, "M2MAuto");

    [SkippableFact]
    public async Task CompositeKeySingleEagerPairsBothKeyComponents()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.SingleEagerPairsBothKeyComponentsAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [SkippableFact]
    public async Task CompositeKeyAllEagerAssemblesEachPostsTags()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.AllEagerAssemblesEachPostsTagsAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [SkippableFact]
    public async Task CompositeKeyAllEagerIncludingDeletedKeepsChildFilters()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.AllEagerIncludingDeletedKeepsChildFiltersAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [SkippableFact]
    public async Task CompositeKeyEagerExcludesGloballyFilteredTags()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CompositeHarnessAsync();
        await ManyToManyExtensionAssertions.CompositeEagerExcludesGloballyFilteredTagsAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>());
    }

    [SkippableFact]
    public async Task CompositeKeyEagerLoadCostsOneRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            _fixture.AdminConnectionString, FeatureSchema.CompositeManyToManyMySqlDdl, "M2MCompositeGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.CompositeEagerLoadCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>(),
            probe, recorder);
    }

    [SkippableFact]
    public async Task CompositeKeyAllEagerCostsOneRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            _fixture.AdminConnectionString, FeatureSchema.CompositeManyToManyMySqlDdl, "M2MCompositeAllGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.CompositeAllEagerCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MPostStore>(),
            harness.GetRequiredService<M2MTagStore>(),
            harness.GetRequiredService<M2MPostTagStore>(),
            probe, recorder);
    }

    [SkippableFact]
    public async Task AutoJunctionSingleEagerReadsThroughTheSynthesizedTable()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionSingleEagerReadsThroughSynthesizedTableAsync(
            harness.GetRequiredService<M2MAuthorStore>());
    }

    [SkippableFact]
    public async Task AutoJunctionAllEagerAssemblesFromBothSides()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionAllEagerAssemblesFromBothSidesAsync(
            harness.GetRequiredService<M2MAuthorStore>(),
            harness.GetRequiredService<M2MBookStore>());
    }

    [SkippableFact]
    public async Task AutoJunctionAllEagerIncludingDeletedReturnsDeletedParents()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionAllEagerIncludingDeletedAsync(
            harness.GetRequiredService<M2MAuthorStore>());
    }

    [SkippableFact]
    public async Task AutoJunctionEagerExcludesGloballyFilteredBooks()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await AutoHarnessAsync();
        await ManyToManyExtensionAssertions.AutoJunctionEagerExcludesGloballyFilteredBooksAsync(
            harness.GetRequiredService<M2MAuthorStore>(),
            harness.GetRequiredService<M2MBookStore>());
    }

    [SkippableFact]
    public async Task AutoJunctionEagerLoadCostsOneRoundTrip()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var (harness, probe, recorder) = await CreateGridHarnessAsync(
            _fixture.AdminConnectionString, FeatureSchema.AutoJunctionMySqlDdl, "M2MAutoGrid");
        await using var _ = harness;
        await ManyToManyExtensionAssertions.AutoJunctionEagerLoadCostsOneRoundTripAsync(
            harness.GetRequiredService<M2MAuthorStore>(), probe, recorder);
    }

    [Fact]
    public void GeneratedSqlNamesTheSynthesizedJunctionTheDdlCreates()
        => ManyToManyExtensionAssertions.GeneratedSqlNamesTheSynthesizedJunction();
}
