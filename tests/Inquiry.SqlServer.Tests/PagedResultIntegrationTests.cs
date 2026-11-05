using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Generated <c>InquiryPagedResult&lt;T&gt;</c> methods against real SqlServer: the page SELECT and the COUNT
/// travel as one multi-result command (#403). The assertions live in <see cref="PagedResultAssertions"/>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PagedResultIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;

    public PagedResultIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task PageAndTotalTravelAsOneCommand()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.PageAndTotalTravelAsOneCommandAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PagePastTheEndKeepsTheTotal()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.PagePastTheEndKeepsTheTotalAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task EmptyTableReturnsNoItemsAndZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.EmptyTableReturnsNoItemsAndZeroAsync(Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PredicateFiltersBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.PredicateFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task NotInCollectionFiltersBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.NotInCollectionFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PagedResultRunsInsideATransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.SqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.PagedResultRunsInsideATransactionAsync(h.GetRequiredService<IInquiry>(), Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task GlobalFiltersApplyToBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(FeatureSchema.TenantScopedDocSqlServerDdl);
        await using var _ = h;

        await PagedResultAssertions.GlobalFiltersApplyToBothStatementsAsync(h.GetRequiredService<TenantScopedDocStore>(), probe, recorder);
    }

    private static CategoryStore Categories(SqlServerTestHarness harness) => harness.GetRequiredService<CategoryStore>();

    private static ProductStore Products(SqlServerTestHarness harness) => harness.GetRequiredService<ProductStore>();

    private async Task<(SqlServerTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync(string ddl)
    {
        var probe = PagedResultAssertions.CreateProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "paged", configureServices: Configure(probe, recorder));
        return (harness, probe, recorder);
    }

    private static Action<IServiceCollection> Configure(BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
        => services =>
        {
            services.AddSingleton<IInquiryCommandInterceptor>(recorder);
            probe.Decorate(services);
        };
}
