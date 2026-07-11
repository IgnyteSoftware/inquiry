using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("Gadget")]
public sealed class Gadget
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;
}

public partial class GadgetStore : InquiryStore<Gadget>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Gadget gadget, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);

    [InquiryDeleteAll]
    public partial Task<int> DeleteAllAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}

/// <summary>Batch delete end-to-end against SQL Server: DELETE ... WHERE Id IN (...) removes exactly the
/// listed rows; an empty collection is a no-op.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class BatchDeleteIntegrationTests
{
    private const string Ddl = Inquiry.Generated.InquiryGeneratedSchema.ProviderArtifactsDdl
        + "CREATE TABLE [Gadget] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL);";

    private readonly SqlServerContainerFixture _fixture;
    public BatchDeleteIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private async Task<SqlServerTestHarness> SeedAsync()
    {
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "batchdel");
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

        var affected = await store.DeleteAllAsync(new int[] { 1, 3, 5 });

        Assert.Equal(3, affected);
        Assert.Equal(2L, await store.CountAsync());
    }

    [SkippableFact]
    public async Task EmptyCollectionIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<GadgetStore>();

        Assert.Equal(0, await store.DeleteAllAsync(System.Array.Empty<int>()));
        Assert.Equal(5L, await store.CountAsync());
    }
}
