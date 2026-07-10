using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("Sku")]
public sealed class Sku
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Price")]
    public decimal Price { get; set; }
}

public partial class SkuStore : InquiryStore<Sku>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Sku product, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Sku?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquiryUpdateAll]
    public partial Task<int> UpdateAllAsync(IEnumerable<Sku> products, CancellationToken cancellationToken = default);
}

/// <summary>Batch update end-to-end against SQL Server: one UPDATE per row in a single command updates
/// each row by key; an empty collection is a no-op.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class BatchUpdateIntegrationTests
{
    private const string Ddl = "CREATE TABLE [Sku] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL, [Price] DECIMAL(18,2) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;
    public BatchUpdateIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private async Task<SqlServerTestHarness> SeedAsync()
    {
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "batchupd");
        var store = harness.GetRequiredService<SkuStore>();
        await store.InsertAsync(new Sku { Name = "A", Price = 1m });
        await store.InsertAsync(new Sku { Name = "B", Price = 2m });
        await store.InsertAsync(new Sku { Name = "C", Price = 3m });
        return harness;
    }

    [SkippableFact]
    public async Task UpdatesEachRowByKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<SkuStore>();

        var affected = await store.UpdateAllAsync(new[]
        {
            new Sku { Id = 1, Name = "A2", Price = 10m },
            new Sku { Id = 3, Name = "C2", Price = 30m },
        });

        Assert.Equal(2, affected);
        Assert.Equal("A2", (await store.GetAsync(1))!.Name);
        Assert.Equal(10m, (await store.GetAsync(1))!.Price);
        Assert.Equal("B", (await store.GetAsync(2))!.Name); // untouched
        Assert.Equal("C2", (await store.GetAsync(3))!.Name);
    }

    [SkippableFact]
    public async Task EmptyCollectionIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<SkuStore>();

        Assert.Equal(0, await store.UpdateAllAsync(System.Array.Empty<Sku>()));
        Assert.Equal("A", (await store.GetAsync(1))!.Name);
    }
}
