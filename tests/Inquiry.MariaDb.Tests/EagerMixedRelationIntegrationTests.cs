using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.Tests;

/// <summary>
/// #70: a parent carrying BOTH a to-one reference and a to-many collection still resolves in a single
/// round trip — one command, three result sets, read through the InquiryGridReader.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class EagerMixedRelationIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;

    public EagerMixedRelationIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectOneByKeyEagerWithTwoRelationsIssuesOneCommand()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (harness, probe, recorder) = await CreateHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectOneByKeyEagerWithTwoRelationsIssuesOneCommandAsync(
            harness.GetRequiredService<EagerMixedAuthorStore>(),
            harness.GetRequiredService<EagerMixedPostStore>(),
            harness.GetRequiredService<EagerMixedTagStore>(),
            probe, recorder);
    }

    [SkippableFact]
    public async Task SelectAllEagerWithTwoRelationsIssuesOneCommand()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (harness, probe, recorder) = await CreateHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectAllEagerWithTwoRelationsIssuesOneCommandAsync(
            harness.GetRequiredService<EagerMixedAuthorStore>(),
            harness.GetRequiredService<EagerMixedPostStore>(),
            harness.GetRequiredService<EagerMixedTagStore>(),
            probe, recorder);
    }

    private async Task<(MariaDbTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync()
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.EagerMixedMySqlDdl, "eagermixed",
            configureServices: Configure(probe, recorder));
        return (harness, probe, recorder);
    }

    private static Action<IServiceCollection> Configure(BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
        => services =>
        {
            services.AddSingleton<IInquiryCommandInterceptor>(recorder);
            probe.Decorate(services);
        };
}
