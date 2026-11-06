using Inquiry.Interceptors;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.InLists;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Live #509 checks: an IN list up to the dialect threshold binds as padded scalar parameters, and longer,
/// empty and negated lists keep the collection transport. The scenarios live in tests/Shared.
/// </summary>
public sealed class SmallInListIntegrationTests
{
    // Matches the dialect's ScalarInListMaxLength.
    private const int ScalarLimit = 4;
    private const string TransportMarker = "json_each(";
    private const string Ddl = "CREATE TABLE \"SmallInItem\" (\"Id\" INTEGER PRIMARY KEY, \"CategoryId\" INTEGER NULL, \"Code\" TEXT NULL)";

    private static Task<SqliteTestHarness> CreateAsync(RecordingCommandInterceptor recorder)
        => SqliteTestHarness.CreateAsync(Ddl, "SmallIn", foreignKeys: false, configureServices: services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));

    [Fact]
    public async Task ShortListsBindOnePaddedBucketEach()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.ShortListsBindScalarBucketsAsync(store, recorder, ScalarLimit, TransportMarker);
    }

    [Fact]
    public async Task EmptyAndNegatedListsKeepTheirMeaning()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.EmptyAndNegatedListsKeepTheirMeaningAsync(store, ScalarLimit);
    }

    [Fact]
    public async Task PagedTotalsMatchOnBothPaths()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.PagedTotalsMatchOnBothPathsAsync(store, ScalarLimit);
    }

    [Fact]
    public async Task TwoListsBindIndependently()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.TwoListsBindIndependentlyAsync(store, ScalarLimit);
    }

    [Fact]
    public async Task StringListsMatchOnBothPaths()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.StringListsMatchOnBothPathsAsync(store, ScalarLimit);
    }

    [Fact]
    public async Task DeletesRemoveListedRowsOnBothPaths()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var harness = await CreateAsync(recorder);
        var store = harness.GetRequiredService<SmallInItemStore>();
        await SmallInListScenarios.DeletesRemoveListedRowsOnBothPathsAsync(store, ScalarLimit);
    }
}
