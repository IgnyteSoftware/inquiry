using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("TvpProcItem")]
public sealed class TvpProcItem
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn]
    public int GroupId { get; set; }
}

public partial class TvpProcItemStore : InquiryStore<TvpProcItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(TvpProcItem item, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_SumGroupIds", OutputParameter = "Total")]
    public partial Task<int> SumGroupIdsAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[Inquiry_IntList]")] IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_CountByIds", ReturnsValue = true)]
    public partial Task<int> CountByIdsAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[Inquiry_IntList]")] IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("usp_FilterByIdsAndGroup", OutputParameter = "MatchCount")]
    public partial Task<int> FilterByIdsAndGroupAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[Inquiry_IntList]")] IEnumerable<int> ids,
        int groupId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stored-procedure TVP parameters against real SQL Server: exercises the generated
/// <c>InquiryTvpParameter.Bind</c> path for procedures with TVP-only, mixed TVP+scalar,
/// empty/null collections, and multiple distinct values.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureTvpIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public StoredProcedureTvpIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TYPE [dbo].[Inquiry_IntList] AS TABLE ([Value] INT NOT NULL);
        CREATE TABLE TvpProcItem (Id INT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(50) NOT NULL, GroupId INT NOT NULL);
        EXEC('CREATE PROCEDURE usp_SumGroupIds @ids [dbo].[Inquiry_IntList] READONLY, @Total INT OUTPUT AS BEGIN SET NOCOUNT ON; SELECT @Total = COALESCE(SUM(p.GroupId), 0) FROM TvpProcItem p INNER JOIN @ids t ON p.Id = t.[Value]; END');
        EXEC('CREATE PROCEDURE usp_CountByIds @ids [dbo].[Inquiry_IntList] READONLY AS BEGIN SET NOCOUNT ON; DECLARE @n INT; SELECT @n = COUNT(*) FROM TvpProcItem p INNER JOIN @ids t ON p.Id = t.[Value]; RETURN @n; END');
        EXEC('CREATE PROCEDURE usp_FilterByIdsAndGroup @ids [dbo].[Inquiry_IntList] READONLY, @GroupId INT, @MatchCount INT OUTPUT AS BEGIN SET NOCOUNT ON; SELECT @MatchCount = COUNT(*) FROM TvpProcItem p INNER JOIN @ids t ON p.Id = t.[Value] WHERE p.GroupId = @GroupId; END');
        """;

    [SkippableFact]
    public async Task TvpOutputParameterRoundTripsMultipleDistinctValues()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        await store.InsertAsync(new TvpProcItem { Name = "A", GroupId = 10 });
        await store.InsertAsync(new TvpProcItem { Name = "B", GroupId = 20 });
        await store.InsertAsync(new TvpProcItem { Name = "C", GroupId = 30 });

        Assert.Equal(30, await store.SumGroupIdsAsync([1, 2]));
        Assert.Equal(60, await store.SumGroupIdsAsync([1, 2, 3]));
        Assert.Equal(10, await store.SumGroupIdsAsync([1]));
    }

    [SkippableFact]
    public async Task TvpReturnValueRoundTripsMultipleDistinctValues()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        await store.InsertAsync(new TvpProcItem { Name = "A", GroupId = 10 });
        await store.InsertAsync(new TvpProcItem { Name = "B", GroupId = 20 });
        await store.InsertAsync(new TvpProcItem { Name = "C", GroupId = 30 });

        Assert.Equal(2, await store.CountByIdsAsync([1, 2]));
        Assert.Equal(3, await store.CountByIdsAsync([1, 2, 3]));
        Assert.Equal(1, await store.CountByIdsAsync([1]));
    }

    [SkippableFact]
    public async Task EmptyCollectionBindsZeroRowsThroughProcedurePath()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        await store.InsertAsync(new TvpProcItem { Name = "A", GroupId = 10 });

        Assert.Equal(0, await store.SumGroupIdsAsync([]));
        Assert.Equal(0, await store.CountByIdsAsync([]));
    }

    [SkippableFact]
    public async Task MixedTvpAndScalarParametersBindCorrectly()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        await store.InsertAsync(new TvpProcItem { Name = "A", GroupId = 10 });
        await store.InsertAsync(new TvpProcItem { Name = "B", GroupId = 10 });
        await store.InsertAsync(new TvpProcItem { Name = "C", GroupId = 20 });

        Assert.Equal(2, await store.FilterByIdsAndGroupAsync([1, 2, 3], 10));
        Assert.Equal(1, await store.FilterByIdsAndGroupAsync([1, 2, 3], 20));
        Assert.Equal(0, await store.FilterByIdsAndGroupAsync([1, 2, 3], 99));
        Assert.Equal(0, await store.FilterByIdsAndGroupAsync([], 10));
    }

    [SkippableFact]
    public async Task TvpProcedureWorksInsideAmbientTransaction()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        await store.InsertAsync(new TvpProcItem { Name = "A", GroupId = 10 });
        await store.InsertAsync(new TvpProcItem { Name = "B", GroupId = 20 });

        var inquiry = harness.GetRequiredService<IInquiry>();
        await using var tx = await inquiry.BeginTransactionAsync();
        var sum = await store.SumGroupIdsAsync([1, 2]);
        await tx.CommitAsync();

        Assert.Equal(30, sum);
    }

    [SkippableFact]
    public async Task NonExistentIdsReturnZero()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, Ddl, "sproctvp", provisionProviderArtifacts: false);
        var store = harness.GetRequiredService<TvpProcItemStore>();

        Assert.Equal(0, await store.SumGroupIdsAsync([999, 1000]));
        Assert.Equal(0, await store.CountByIdsAsync([999]));
    }
}
