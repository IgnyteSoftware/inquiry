using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("PricedItem", GenerateDdl = false)]
public sealed class PricedItem
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn(SqlType = "decimal(19,4)")]
    public decimal Amount { get; set; }

    [InquiryColumn(SqlType = "rowversion", UseDatabaseDefault = true)]
    public byte[] Stamp { get; set; } = [];
}

public partial class PricedItemStore : InquiryStore<PricedItem>
{
    [InquiryInsert]
    public partial Task<PricedItem?> InsertAsync(PricedItem item, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<PricedItem?> GetAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// A returning insert passes each column through the <c>@_out</c> table variable. That table must
/// take the column's declared <c>SqlType</c>, or the returned entity differs from the stored row.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReturningDeclaredTypeIntegrationTests
{
    private const string Ddl = "CREATE TABLE PricedItem (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, Amount DECIMAL(19,4) NOT NULL, Stamp ROWVERSION NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;

    public ReturningDeclaredTypeIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ReturningInsertKeepsTheDeclaredPrecisionAndReadsARowVersion()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "returningtype");
        var store = harness.GetRequiredService<PricedItemStore>();

        var inserted = await store.InsertAsync(new PricedItem { Amount = 12.3456m });

        Assert.NotNull(inserted);
        Assert.Equal(12.3456m, inserted.Amount);
        Assert.Equal(8, inserted.Stamp.Length);
        var stored = await store.GetAsync(inserted.Id);
        Assert.Equal(inserted.Amount, stored!.Amount);
        Assert.Equal(inserted.Stamp, stored.Stamp);
    }
}
