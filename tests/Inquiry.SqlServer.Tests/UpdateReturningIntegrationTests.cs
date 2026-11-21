using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Inquiry.Tests.UpdateReturning;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("ReturningAccount")]
public sealed class ReturningAccount
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn(SqlType = "decimal(19,4)")]
    public decimal Balance { get; set; }
}

public partial class ReturningAccountStore : InquiryStore<ReturningAccount>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(ReturningAccount account, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    [InquirySet(nameof(ReturningAccount.Balance), "{Balance} + @amount")]
    [InquiryWhere(nameof(ReturningAccount.Id))]
    public partial Task<decimal?> DepositAsync(decimal amount, int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Set-based updates that return changed values, read with SQL Server's OUTPUT INSERTED into a typed table
/// variable.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UpdateReturningIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE [ReturningCounter] ([Id] INT PRIMARY KEY, [Category] NVARCHAR(50) NOT NULL, [Hits] INT NOT NULL); " +
        "CREATE TABLE [ReturningAccount] ([Id] INT PRIMARY KEY, [Balance] DECIMAL(19,4) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;
    public UpdateReturningIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private Task<SqlServerTestHarness> CreateAsync()
        => SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "updreturning");

    [SkippableFact]
    public async Task SingleValueReturnsTheNewValueOrNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertSingleValueReturnsTheNewValueOrNullAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [SkippableFact]
    public async Task ListReturnsOneEntryPerUpdatedRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertListReturnsOneEntryPerUpdatedRowAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    [SkippableFact]
    public async Task RowsShowAChangedFilterColumn()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        await UpdateReturningScenarios.AssertRowsShowAChangedFilterColumnAsync(harness.GetRequiredService<ReturningCounterStore>());
    }

    // The inferred DECIMAL(18,2) would round 0.0001 away. The table variable must take the declared SqlType.
    [SkippableFact]
    public async Task ReturnedValueKeepsTheDeclaredSqlType()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateAsync();
        var store = harness.GetRequiredService<ReturningAccountStore>();
        await store.InsertAsync(new ReturningAccount { Id = 1, Balance = 1.2345m });

        Assert.Equal(1.2346m, await store.DepositAsync(0.0001m, 1));
    }
}
