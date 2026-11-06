using Inquiry.Interceptors;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.InLists;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Live #509 checks: an IN list up to the dialect threshold binds as padded scalar parameters, and longer,
/// empty and negated lists keep the collection transport. The scenarios live in tests/Shared.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class SmallInListIntegrationTests
{
    // Matches the dialect's ScalarInListMaxLength.
    private const int ScalarLimit = 0;
    private const string TransportMarker = "= ANY(";
    private const string Ddl = "CREATE TABLE \"SmallInItem\" (\"Id\" INT PRIMARY KEY, \"CategoryId\" INT NULL, \"Code\" VARCHAR(20) NULL)";

    private readonly PostgreSqlContainerFixture _fixture;
    public SmallInListIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private Task<PostgreSqlTestHarness> CreateAsync(RecordingCommandInterceptor recorder)
        => PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "smallin", configureServices: services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));

    [SkippableFact]
    public async Task ShortListsBindOnePaddedBucketEach()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.ShortListsBindScalarBucketsAsync(store, recorder, ScalarLimit, TransportMarker);
    }

    [SkippableFact]
    public async Task EmptyAndNegatedListsKeepTheirMeaning()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.EmptyAndNegatedListsKeepTheirMeaningAsync(store, ScalarLimit);
    }

    [SkippableFact]
    public async Task PagedTotalsMatchOnBothPaths()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.PagedTotalsMatchOnBothPathsAsync(store, ScalarLimit);
    }

    [SkippableFact]
    public async Task TwoListsBindIndependently()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.TwoListsBindIndependentlyAsync(store, ScalarLimit);
    }

    [SkippableFact]
    public async Task StringListsMatchOnBothPaths()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.StringListsMatchOnBothPathsAsync(store, ScalarLimit);
    }

    [SkippableFact]
    public async Task DeletesRemoveListedRowsOnBothPaths()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.DeletesRemoveListedRowsOnBothPathsAsync(store, ScalarLimit);
    }
}
