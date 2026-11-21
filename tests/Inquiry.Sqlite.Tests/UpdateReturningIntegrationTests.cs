using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Inquiry.Tests.UpdateReturning;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("ReturningMember")]
public sealed class ReturningMember
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(IsUnique = true)]
    public string Email { get; set; } = string.Empty;

    [InquiryColumn]
    public int Logins { get; set; }
}

public partial class ReturningMemberStore : InquiryStore<ReturningMember>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReturningMember member, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(ReturningMember.Logins), "{Logins} + 1")]
    [InquiryWhere(nameof(ReturningMember.Email))]
    public partial Task<int?> RecordLoginAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>Set-based updates that return changed values, read with SQLite's UPDATE ... RETURNING.</summary>
public sealed class UpdateReturningIntegrationTests
{
    private const string Ddl = "CREATE TABLE ReturningCounter (Id INTEGER PRIMARY KEY, Category TEXT NOT NULL, Hits INTEGER NOT NULL);";

    private static Task<SqliteTestHarness> CreateAsync() => SqliteTestHarness.CreateAsync(Ddl, "updreturning");

    [Fact]
    public async Task SingleValueReturnsTheNewValueOrNull()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertSingleValueReturnsTheNewValueOrNullAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [Fact]
    public async Task ListReturnsOneEntryPerUpdatedRow()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertListReturnsOneEntryPerUpdatedRowAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [Fact]
    public async Task RowsShowAChangedFilterColumn()
    {
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertRowsShowAChangedFilterColumnAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    // The DDL deliberately omits the unique index that IsUnique declares.
    [Fact]
    public async Task SingleRowReturnThrowsWhenADeclaredUniqueColumnMatchesTwoRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(
            "CREATE TABLE ReturningMember (Id INTEGER PRIMARY KEY, Email TEXT NOT NULL, Logins INTEGER NOT NULL);", "updreturningdup");
        var store = harness.GetRequiredService<ReturningMemberStore>();
        await store.InsertAsync(new ReturningMember { Id = 1, Email = "a@example.com" });
        await store.InsertAsync(new ReturningMember { Id = 2, Email = "a@example.com" });
        await store.InsertAsync(new ReturningMember { Id = 3, Email = "b@example.com" });

        Assert.Equal(1, await store.RecordLoginAsync("b@example.com"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordLoginAsync("a@example.com"));
    }
}
