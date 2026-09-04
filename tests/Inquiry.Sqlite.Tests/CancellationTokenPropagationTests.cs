using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class CancellationTokenPropagationTests
{
    private static CancellationToken PreCancelled => new(canceled: true);

    [Fact]
    public async Task GeneratedSelectAll_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelSelectAll");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SelectAllAsync(PreCancelled));
    }

    [Fact]
    public async Task GeneratedSelectByKey_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelSelectKey");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SelectByKeyAsync("ALFKI", PreCancelled));
    }

    [Fact]
    public async Task GeneratedInsert_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelInsert");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InsertAsync(new Customer { CustomerID = "CANC1", CompanyName = "Cancelled" }, PreCancelled));
    }

    [Fact]
    public async Task GeneratedUpdate_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelUpdate");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.UpdateAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Updated" }, PreCancelled));
    }

    [Fact]
    public async Task GeneratedDelete_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelDelete");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.DeleteByKeyAsync("ALFKI", PreCancelled));
    }

    [Fact]
    public async Task GeneratedUpsert_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelUpsert");
        var store = harness.GetRequiredService<CustomerStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.UpsertAsync(new Customer { CustomerID = "CANC1", CompanyName = "Cancelled" }, PreCancelled));
    }

    [Fact]
    public async Task GeneratedStreaming_PreCancelled_ThrowsOnFirstMoveNext()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelStream");
        var store = harness.GetRequiredService<OrderStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in store.SelectAllAsync(PreCancelled))
            {
            }
        });
    }

    [Fact]
    public async Task IInquiry_QueryListAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelQueryList");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.QueryListAsync<Customer>($"SELECT * FROM Customers", PreCancelled));
    }

    [Fact]
    public async Task IInquiry_QuerySingleOrDefaultAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelQuerySingle");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.QuerySingleOrDefaultAsync<Customer>($"SELECT * FROM Customers WHERE CustomerID = {"ALFKI"}", PreCancelled));
    }

    [Fact]
    public async Task IInquiry_QueryAsync_PreCancelled_ThrowsOnFirstMoveNext()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelQueryAsync");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in inquiry.QueryAsync<Customer>($"SELECT * FROM Customers", PreCancelled))
            {
            }
        });
    }

    [Fact]
    public async Task IInquiry_ExecuteAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelExecute");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteAsync($"DELETE FROM Customers WHERE CustomerID = {"NONE"}", PreCancelled));
    }

    [Fact]
    public async Task IInquiry_ExecuteScalarAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelScalar");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Customers", PreCancelled));
    }

    [Fact]
    public async Task IInquiry_BeginTransactionAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelBeginTx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var tx = await inquiry.BeginTransactionAsync(cancellationToken: PreCancelled);
        });
    }

    [Fact]
    public async Task IInquiry_ExecuteInTransactionAsync_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelExecTx");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteInTransactionAsync(
                async (tx, ct) => await inquiry.ExecuteAsync($"DELETE FROM Customers WHERE CustomerID = {"NONE"}", ct),
                cancellationToken: PreCancelled));
    }

    [Fact]
    public async Task GeneratedBatchInsert_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelBatchInsert");
        var store = harness.GetRequiredService<RegionStore>();

        var regions = new[] { new Region { RegionID = 100, RegionDescription = "Test" } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.InsertAllAsync(regions, PreCancelled));
    }

    [Fact]
    public async Task GeneratedBatchDelete_PreCancelled_Throws()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelBatchDelete");
        var store = harness.GetRequiredService<RegionStore>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.DeleteByKeysAsync(new[] { 1, 2 }, PreCancelled));
    }

    [Fact]
    public async Task GeneratedStreaming_CancelledMidIteration_StopsEnumeration()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelMidStream");
        var store = harness.GetRequiredService<CustomerStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        await store.InsertAsync(new Customer { CustomerID = "AAA01", CompanyName = "A1" });
        await store.InsertAsync(new Customer { CustomerID = "AAA02", CompanyName = "A2" });
        await store.InsertAsync(new Customer { CustomerID = "AAA03", CompanyName = "A3" });

        using var cts = new CancellationTokenSource();
        var orderStore = harness.GetRequiredService<OrderStore>();

        await inquiry.ExecuteAsync($"INSERT INTO Orders (CustomerID) VALUES ({"AAA01"})");
        await inquiry.ExecuteAsync($"INSERT INTO Orders (CustomerID) VALUES ({"AAA02"})");
        await inquiry.ExecuteAsync($"INSERT INTO Orders (CustomerID) VALUES ({"AAA03"})");

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var order in orderStore.SelectAllAsync(cts.Token))
            {
                count++;
                if (count == 1)
                    cts.Cancel();
            }
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IInquiry_StreamingQuery_CancelledMidIteration_StopsEnumeration()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "CancelMidIInquiryStream");
        var inquiry = harness.GetRequiredService<IInquiry>();
        var store = harness.GetRequiredService<CustomerStore>();

        await store.InsertAsync(new Customer { CustomerID = "BBB01", CompanyName = "B1" });
        await store.InsertAsync(new Customer { CustomerID = "BBB02", CompanyName = "B2" });
        await store.InsertAsync(new Customer { CustomerID = "BBB03", CompanyName = "B3" });

        using var cts = new CancellationTokenSource();
        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var customer in inquiry.QueryAsync<Customer>($"SELECT * FROM Customers ORDER BY CustomerID", cts.Token))
            {
                count++;
                if (count == 1)
                    cts.Cancel();
            }
        });

        Assert.Equal(1, count);
    }
}
