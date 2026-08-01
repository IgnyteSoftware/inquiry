using Inquiry.Entities;
using Inquiry.Oracle.Tests.Fixtures;
using Inquiry.Stores;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

[InquiryTable("TRefCursorItem")]
public sealed class RefCursorItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("ItemName")]
    public string ItemName { get; set; } = string.Empty;
}

public sealed partial class OracleRefCursorStore : InquiryStore<RefCursorItem>
{
    [InquiryStoredProcedure("GET_ITEMS_BY_CATEGORY")]
    public partial IAsyncEnumerable<RefCursorItem> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    [InquiryStoredProcedure("GET_ALL_ITEMS")]
    public partial Task<RefCursorItem?> GetFirstItemAsync(CancellationToken cancellationToken = default);
}

[Collection(OracleCollection.Name)]
public sealed class RefCursorStoredProcedureIntegrationTests
{
    private readonly OracleContainerFixture _fixture;

    public RefCursorStoredProcedureIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task RefCursorStreamingProcedureReturnsEntities()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            "CREATE TABLE TRefCursorItem (Id NUMBER(10) PRIMARY KEY, ItemName VARCHAR2(100), CategoryId NUMBER(10))",
            "refcursor");

        await using (var connection = new OracleConnection(harness.ConnectionString))
        {
            await connection.OpenAsync();

            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT ALL
                        INTO TRefCursorItem (Id, ItemName, CategoryId) VALUES (1, 'Widget', 10)
                        INTO TRefCursorItem (Id, ItemName, CategoryId) VALUES (2, 'Gadget', 10)
                        INTO TRefCursorItem (Id, ItemName, CategoryId) VALUES (3, 'Gizmo',  20)
                    SELECT 1 FROM DUAL
                    """;
                await insert.ExecuteNonQueryAsync();
            }

            await using (var proc = connection.CreateCommand())
            {
                proc.CommandText = """
                    CREATE OR REPLACE PROCEDURE GET_ITEMS_BY_CATEGORY(
                        p_category_id IN NUMBER,
                        p_cursor      OUT SYS_REFCURSOR)
                    AS
                    BEGIN
                        OPEN p_cursor FOR
                            SELECT Id, ItemName FROM TRefCursorItem WHERE CategoryId = p_category_id ORDER BY Id;
                    END;
                    """;
                await proc.ExecuteNonQueryAsync();
            }
        }

        var store = harness.GetRequiredService<OracleRefCursorStore>();
        var items = new List<RefCursorItem>();
        await foreach (var item in store.GetByCategoryAsync(10))
        {
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        Assert.Equal("Widget", items[0].ItemName);
        Assert.Equal("Gadget", items[1].ItemName);
    }

    [SkippableFact]
    public async Task RefCursorSingleEntityProcedureReturnsSingleRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            "CREATE TABLE TRefCursorItem (Id NUMBER(10) PRIMARY KEY, ItemName VARCHAR2(100), CategoryId NUMBER(10))",
            "refcursor_single");

        await using (var connection = new OracleConnection(harness.ConnectionString))
        {
            await connection.OpenAsync();

            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO TRefCursorItem (Id, ItemName, CategoryId) VALUES (1, 'Solo', 10)";
                await insert.ExecuteNonQueryAsync();
            }

            await using (var proc = connection.CreateCommand())
            {
                proc.CommandText = """
                    CREATE OR REPLACE PROCEDURE GET_ALL_ITEMS(
                        p_cursor OUT SYS_REFCURSOR)
                    AS
                    BEGIN
                        OPEN p_cursor FOR SELECT Id, ItemName FROM TRefCursorItem ORDER BY Id;
                    END;
                    """;
                await proc.ExecuteNonQueryAsync();
            }
        }

        var store = harness.GetRequiredService<OracleRefCursorStore>();
        var item = await store.GetFirstItemAsync();

        Assert.NotNull(item);
        Assert.Equal("Solo", item.ItemName);
    }
}
