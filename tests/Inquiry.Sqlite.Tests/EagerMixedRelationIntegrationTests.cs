using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// #70: a parent carrying BOTH a to-one reference and a to-many collection still resolves in a single
/// round trip — one command, three result sets, read through the InquiryGridReader.
/// </summary>
public sealed class EagerMixedRelationIntegrationTests
{
    [Fact]
    public async Task SelectOneByKeyEagerWithTwoRelationsIssuesOneCommand()
    {
        var (harness, probe, recorder) = await CreateHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectOneByKeyEagerWithTwoRelationsIssuesOneCommandAsync(
            harness.GetRequiredService<EagerMixedAuthorStore>(),
            harness.GetRequiredService<EagerMixedPostStore>(),
            harness.GetRequiredService<EagerMixedTagStore>(),
            probe, recorder);
    }

    [Fact]
    public async Task SelectAllEagerWithTwoRelationsIssuesOneCommand()
    {
        var (harness, probe, recorder) = await CreateHarnessAsync();
        await using var _ = harness;

        await EagerGridCommandAssertions.SelectAllEagerWithTwoRelationsIssuesOneCommandAsync(
            harness.GetRequiredService<EagerMixedAuthorStore>(),
            harness.GetRequiredService<EagerMixedPostStore>(),
            harness.GetRequiredService<EagerMixedTagStore>(),
            probe, recorder);
    }

    private static async Task<(SqliteTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync()
    {
        var probe = new BatchExecutionProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqliteTestHarness.CreateAsync(
            FeatureSchema.EagerMixedSqliteDdl, "EagerMixed",
            configureServices: s =>
            {
                s.AddSingleton<IInquiryCommandInterceptor>(recorder);
                probe.Decorate(s);
            });
        return (harness, probe, recorder);
    }
}
