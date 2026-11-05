using Inquiry.FeatureCatalog;
using Inquiry.Interceptors;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Testing;
using Inquiry.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.Tests;

/// <summary>
/// Generated <c>InquiryPagedResult&lt;T&gt;</c> methods against real PostgreSql: the page SELECT and the COUNT
/// travel as one multi-result command (#403). The assertions live in <see cref="PagedResultAssertions"/>.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PagedResultIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public PagedResultIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task PageAndTotalTravelAsOneCommand()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.PageAndTotalTravelAsOneCommandAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PagePastTheEndKeepsTheTotal()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.PagePastTheEndKeepsTheTotalAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task EmptyTableReturnsNoItemsAndZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.EmptyTableReturnsNoItemsAndZeroAsync(Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PredicateFiltersBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.PredicateFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task NotInCollectionFiltersBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.NotInCollectionFiltersBothStatementsAsync(Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task PagedResultRunsInsideATransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(NorthwindSchema.PostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.PagedResultRunsInsideATransactionAsync(h.GetRequiredService<IInquiry>(), Categories(h), Products(h), probe, recorder);
    }

    [SkippableFact]
    public async Task GlobalFiltersApplyToBothStatements()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var (h, probe, recorder) = await CreateHarnessAsync(FeatureSchema.TenantScopedDocPostgreSqlDdl);
        await using var _ = h;

        await PagedResultAssertions.GlobalFiltersApplyToBothStatementsAsync(h.GetRequiredService<TenantScopedDocStore>(), probe, recorder);
    }

    private static CategoryStore Categories(PostgreSqlTestHarness harness) => harness.GetRequiredService<CategoryStore>();

    private static ProductStore Products(PostgreSqlTestHarness harness) => harness.GetRequiredService<ProductStore>();

    private async Task<(PostgreSqlTestHarness Harness, BatchExecutionProbe Probe, RecordingCommandInterceptor Recorder)> CreateHarnessAsync(string ddl)
    {
        var probe = PagedResultAssertions.CreateProbe();
        var recorder = new RecordingCommandInterceptor();
        var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "paged", configureServices: Configure(probe, recorder));
        return (harness, probe, recorder);
    }

    private static Action<IServiceCollection> Configure(BatchExecutionProbe probe, RecordingCommandInterceptor recorder)
        => services =>
        {
            services.AddSingleton<IInquiryCommandInterceptor>(recorder);
            probe.Decorate(services);
        };
}
