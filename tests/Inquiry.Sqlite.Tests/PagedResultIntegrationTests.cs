using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Generated <c>InquiryPagedResult&lt;T&gt;</c> methods against real Sqlite: the page SELECT and the COUNT
/// travel as one multi-result command (#403). The assertions live in <see cref="PagedResultAssertions"/>.
/// </summary>
public sealed class PagedResultIntegrationTests
{
    [Fact]
    public async Task PageAndTotalTravelAsOneCommand()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.PageAndTotalTravelAsOneCommandAsync(Categories(h), Products(h), probe, recorder);
    }

    [Fact]
    public async Task PagePastTheEndKeepsTheTotal()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.PagePastTheEndKeepsTheTotalAsync(Categories(h), Products(h), probe, recorder);
    }

    [Fact]
    public async Task EmptyTableReturnsNoItemsAndZero()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.EmptyTableReturnsNoItemsAndZeroAsync(Products(h), probe, recorder);
    }

    [Fact]
    public async Task PredicateFiltersBothStatements()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.PredicateFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [Fact]
    public async Task NotInCollectionFiltersBothStatements()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.NotInCollectionFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [Fact]
    public async Task PagedResultRunsInsideATransaction()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.PagedResultRunsInsideATransactionAsync(h.GetRequiredService<IInquiry>(), Categories(h), Products(h), probe, recorder);
    }

    [Fact]
    public async Task GlobalFiltersApplyToBothStatements()
    {
        var (h, probe, recorder) = await CreateHarnessAsync(FeatureSchema.TenantScopedDocSqliteDdl);
        await using var _ = h;

        await PagedResultAssertions.GlobalFiltersApplyToBothStatementsAsync(h.GetRequiredService<TenantScopedDocStore>(), probe, recorder);
    }

    private static CategoryStore Categories(SqliteTestHarness harness) => harness.GetRequiredService<CategoryStore>();

    private static ProductStore Products(SqliteTestHarness harness) => harness.GetRequiredService<ProductStore>();

    private static async Task<(SqliteTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync(string ddl)
    {
        var probe = PagedResultAssertions.CreateProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqliteTestHarness.CreateAsync(ddl, "Paged", configureServices: Configure(probe, recorder));
        return (harness, probe, recorder);
    }

    private static Action<IServiceCollection> Configure(BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
        => services =>
        {
            services.AddSingleton<IInquiryCommandInterceptor>(recorder);
            probe.Decorate(services);
        };
}
