using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// #70: a parent carrying BOTH a to-one reference and a to-many collection still resolves in a single
/// round trip — one command, three result sets, read through the InquiryGridReader.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class EagerMixedRelationIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public EagerMixedRelationIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

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

    private async Task<(SqlServerTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync()
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.EagerMixedSqlServerDdl, "EagerMixed",
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
