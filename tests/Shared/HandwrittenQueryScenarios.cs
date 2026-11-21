using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests.HandwrittenQueries;

// Lower-case names let the same unquoted SQL run on every dialect, because PostgreSQL folds unquoted
// names to lower case and Oracle does not quote these names.
[InquiryTable("query_item")]
public sealed class QueryItem
{
    [InquiryKey("id")]
    public int Id { get; set; }

    [InquiryColumn("name", Length = 50)]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("score")]
    public int Score { get; set; }
}

[InquiryAdHoc]
public sealed class QueryItemSummary
{
    public string Name { get; set; } = string.Empty;

    public int Score { get; set; }
}

public partial class QueryItemStore : InquiryStore<QueryItem>
{
    private const string FromMinScore = " FROM query_item WHERE score >= @minScore AND name <> '@nobody'";

    [InquiryInsert]
    public partial Task<int> InsertAsync(QueryItem item, CancellationToken cancellationToken = default);

    [InquiryQuery("SELECT name AS Name, score AS Score" + FromMinScore + " ORDER BY score")]
    public partial Task<IReadOnlyList<QueryItemSummary>> ListAsync(int minScore, CancellationToken cancellationToken = default);

    [InquiryQuery("SELECT score, id, name FROM query_item WHERE id = @id")]
    public partial Task<QueryItem?> FindAsync(int id, CancellationToken cancellationToken = default);

    [InquiryQuery("SELECT COUNT(*)" + FromMinScore)]
    public partial Task<long> CountAsync(int minScore, CancellationToken cancellationToken = default);

    // The parameters are declared in the opposite order to the SQL, so only name binding passes.
    [InquiryQuery("UPDATE query_item SET score = score + @delta WHERE name = @name")]
    public partial Task<int> AddScoreAsync(string name, int delta, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared live checks for #620. Each <c>[InquiryQuery]</c> return shape runs on the real engine, binds its
/// parameters by name and joins an active <c>ExecuteInTransactionAsync</c> transaction.
/// </summary>
internal static class HandwrittenQueryScenarios
{
    private static async Task SeedAsync(QueryItemStore store)
    {
        await store.InsertAsync(new QueryItem { Id = 1, Name = "alpha", Score = 10 });
        await store.InsertAsync(new QueryItem { Id = 2, Name = "beta", Score = 20 });
        await store.InsertAsync(new QueryItem { Id = 3, Name = "gamma", Score = 30 });
    }

    internal static async Task ReturnShapesBindByNameAsync(QueryItemStore store)
    {
        await SeedAsync(store);

        var rows = await store.ListAsync(20);
        Assert.Equal(["beta", "gamma"], rows.Select(static row => row.Name));
        Assert.Equal([20, 30], rows.Select(static row => row.Score));

        var found = await store.FindAsync(2);
        Assert.NotNull(found);
        Assert.Equal((2, "beta", 20), (found.Id, found.Name, found.Score));
        Assert.Null(await store.FindAsync(99));

        Assert.Equal(2L, await store.CountAsync(20));
        Assert.Equal(1, await store.AddScoreAsync("alpha", 15));
        Assert.Equal(3L, await store.CountAsync(20));
    }

    internal static async Task CallsJoinTheActiveTransactionAsync(IInquiry inquiry, QueryItemStore store)
    {
        await SeedAsync(store);

        var rollback = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync(async _ =>
        {
            Assert.Equal(1, await store.AddScoreAsync("alpha", 100));

            // Only a command on the transaction's connection sees the uncommitted update.
            Assert.Equal(1L, await store.CountAsync(100));
            throw new InvalidOperationException("roll back");
        }));

        Assert.Equal("roll back", rollback.Message);
        Assert.Equal(0L, await store.CountAsync(100));
    }
}
