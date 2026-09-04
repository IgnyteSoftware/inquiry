using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("Gadget")]
public sealed class Gadget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;
}

public partial class GadgetStore : InquiryStore<Gadget>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Gadget gadget, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere("Id", Compare.In)]
    public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
}

/// <summary>Collection predicate delete end-to-end against MariaDB: DELETE ... WHERE Id IN (...) removes exactly the
/// listed rows; an empty collection is a no-op.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class BatchDeleteIntegrationTests
{
    private const string Ddl = "CREATE TABLE `Gadget` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY, `Name` VARCHAR(255) NOT NULL);";

    private readonly MariaDbContainerFixture _fixture;
    public BatchDeleteIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private async Task<MariaDbTestHarness> SeedAsync()
    {
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "batchdel");
        var store = harness.GetRequiredService<GadgetStore>();
        for (var i = 0; i < 5; i++)
        {
            await store.InsertAsync(new Gadget { Name = "G" + i });
        }

        return harness;
    }

    [SkippableFact]
    public async Task DeletesOnlyListedKeys()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<GadgetStore>();

        var affected = await store.DeleteAllAsync(new long[] { 1, 3, 5 });

        Assert.Equal(3, affected);
        Assert.Equal(2L, await store.CountAsync());
    }

    [SkippableFact]
    public async Task EmptyCollectionIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<GadgetStore>();

        Assert.Equal(0, await store.DeleteAllAsync(System.Array.Empty<long>()));
        Assert.Equal(5L, await store.CountAsync());
    }
}
