using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("TProcListItem")]
public sealed class OracleProcListItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("ItemName")]
    public string ItemName { get; set; } = string.Empty;
}

public sealed partial class OracleProcListStore : InquiryStore<OracleProcListItem>
{
    [InquiryStoredProcedure("LIST_PROC_ITEMS")]
    public partial Task<IReadOnlyList<OracleProcListItem>> ListByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A buffered <c>Task&lt;IReadOnlyList&lt;TEntity&gt;&gt;</c> procedure against real Oracle through the
/// generated ref-cursor wrapper. The procedure logs each invocation, so the tests can count
/// executions per call.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class StoredProcedureListIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public StoredProcedureListIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    private static async Task<OracleTestHarness> CreateHarnessAsync(OracleContainerFixture fixture, string prefix)
    {
        var harness = await OracleTestHarness.CreateFromDdlAsync(
            fixture.AdminConnectionString,
            """
            CREATE TABLE TProcListItem (Id NUMBER(10) PRIMARY KEY, ItemName VARCHAR2(100), CategoryId NUMBER(10));
            CREATE TABLE TProcListCall (CalledAt TIMESTAMP NOT NULL)
            """,
            prefix);

        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT ALL
                    INTO TProcListItem (Id, ItemName, CategoryId) VALUES (1, 'Widget', 10)
                    INTO TProcListItem (Id, ItemName, CategoryId) VALUES (2, 'Gadget', 10)
                    INTO TProcListItem (Id, ItemName, CategoryId) VALUES (3, 'Gizmo',  20)
                SELECT 1 FROM DUAL
                """;
            await insert.ExecuteNonQueryAsync();
        }

        await using (var proc = connection.CreateCommand())
        {
            proc.CommandText = """
                CREATE OR REPLACE PROCEDURE LIST_PROC_ITEMS(
                    p_category_id IN NUMBER,
                    p_cursor      OUT SYS_REFCURSOR)
                AS
                BEGIN
                    INSERT INTO TProcListCall (CalledAt) VALUES (SYSTIMESTAMP);
                    OPEN p_cursor FOR
                        SELECT Id, ItemName FROM TProcListItem WHERE CategoryId = p_category_id ORDER BY Id;
                END;
                """;
            await proc.ExecuteNonQueryAsync();
        }

        return harness;
    }

    private static async Task<int> CallCountAsync(OracleTestHarness harness)
    {
        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM TProcListCall";
        return Convert.ToInt32(await count.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [SkippableFact]
    public async Task ListReturnsAllRowsFromOneInvocation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture, "proclist");
        var store = harness.GetRequiredService<OracleProcListStore>();

        var items = await store.ListByCategoryAsync(10);

        Assert.Equal([1, 2], items.Select(static i => i.Id));
        Assert.Equal(["Widget", "Gadget"], items.Select(static i => i.ItemName));
        Assert.Equal(1, await CallCountAsync(harness));

        Assert.Empty(await store.ListByCategoryAsync(99));
        Assert.Equal(2, await CallCountAsync(harness));
    }

    [SkippableFact]
    public async Task ListJoinsExecuteInTransactionAsync()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture, "proclist_tx");
        var store = harness.GetRequiredService<OracleProcListStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        var items = await inquiry.ExecuteInTransactionAsync(_ => store.ListByCategoryAsync(20));

        Assert.Equal("Gizmo", Assert.Single(items).ItemName);
        Assert.Equal(1, await CallCountAsync(harness));

        // A rollback removes the procedure's log row, so the call ran inside the transaction.
        await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync(async _ =>
        {
            Assert.Equal(2, (await store.ListByCategoryAsync(10)).Count);
            throw new InvalidOperationException("roll back");
        }));
        Assert.Equal(1, await CallCountAsync(harness));
    }

    [SkippableFact]
    public async Task ListObservesCancellation()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateHarnessAsync(_fixture, "proclist_cancel");
        var store = harness.GetRequiredService<OracleProcListStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ListByCategoryAsync(10, new CancellationToken(canceled: true)));
        Assert.Equal(0, await CallCountAsync(harness));
    }
}
