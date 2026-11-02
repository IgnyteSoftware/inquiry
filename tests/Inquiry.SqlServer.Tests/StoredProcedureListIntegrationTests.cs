using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("ProcListItem")]
public sealed class ProcListItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(Length = 50)]
    public string Name { get; set; } = string.Empty;
}

public partial class ProcListItemStore : InquiryStore<ProcListItem>
{
    [InquiryStoredProcedure("usp_ListItemsByIds")]
    public partial Task<IReadOnlyList<ProcListItem>> ListByIdsAsync(
        [InquiryParameter(TvpTypeName = "[dbo].[Inquiry_ProcListIds]")] IEnumerable<int> ids,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A buffered <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c> procedure against real SQL Server with a
/// TVP argument. The procedure logs each invocation, so the tests can count executions per call.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureListIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public StoredProcedureListIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TYPE [dbo].[Inquiry_ProcListIds] AS TABLE ([Value] INT NOT NULL);
        CREATE TABLE ProcListItem (Id INT PRIMARY KEY, Name NVARCHAR(50) NOT NULL);
        CREATE TABLE ProcListCall (CallId INT IDENTITY(1,1) PRIMARY KEY);
        INSERT INTO ProcListItem (Id, Name) VALUES (1, N'Widget'), (2, N'Gadget'), (3, N'Gizmo');
        EXEC('CREATE PROCEDURE usp_ListItemsByIds @ids [dbo].[Inquiry_ProcListIds] READONLY AS BEGIN SET NOCOUNT ON; INSERT INTO ProcListCall DEFAULT VALUES; SELECT p.Id, p.Name FROM ProcListItem p INNER JOIN @ids t ON p.Id = t.[Value] ORDER BY p.Id; END');
        """;

    private static Task<SqlServerTestHarness> CreateHarnessAsync(SqlServerContainerFixture fixture)
        => SqlServerTestHarness.CreateFromDdlAsync(fixture.AdminConnectionString, Ddl, "sproclist", provisionProviderArtifacts: false);

    private static Task<int> CallCountAsync(IInquiry inquiry)
        => inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM ProcListCall");

    [SkippableFact]
    public async Task ListReturnsAllRowsFromOneInvocation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture);
        var store = harness.GetRequiredService<ProcListItemStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        var items = await store.ListByIdsAsync([1, 3]);

        Assert.Equal([1, 3], items.Select(static i => i.Id));
        Assert.Equal(["Widget", "Gizmo"], items.Select(static i => i.Name));
        Assert.Equal(1, await CallCountAsync(inquiry));

        Assert.Empty(await store.ListByIdsAsync([]));
        Assert.Equal(2, await CallCountAsync(inquiry));
    }

    [SkippableFact]
    public async Task ListJoinsExecuteInTransactionAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture);
        var store = harness.GetRequiredService<ProcListItemStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        var items = await inquiry.ExecuteInTransactionAsync(_ => store.ListByIdsAsync([1, 2, 3]));

        Assert.Equal([1, 2, 3], items.Select(static i => i.Id));
        Assert.Equal(1, await CallCountAsync(inquiry));

        // A rollback removes the procedure's log row, so the call ran inside the transaction.
        await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync(async _ =>
        {
            Assert.Equal(2, (await store.ListByIdsAsync([1, 2])).Count);
            throw new InvalidOperationException("roll back");
        }));
        Assert.Equal(1, await CallCountAsync(inquiry));
    }

    [SkippableFact]
    public async Task ListObservesCancellation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture);
        var store = harness.GetRequiredService<ProcListItemStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ListByIdsAsync([1], new CancellationToken(canceled: true)));
        Assert.Equal(0, await CallCountAsync(harness.GetRequiredService<IInquiry>()));
    }
}
