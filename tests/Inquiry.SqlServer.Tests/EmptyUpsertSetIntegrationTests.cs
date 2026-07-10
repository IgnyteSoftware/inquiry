using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("SsKeyOnlyItem")]
public sealed class SsKeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }
}

public partial class SsKeyOnlyItemStore : InquiryStore<SsKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(SsKeyOnlyItem item, CancellationToken ct = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<SsKeyOnlyItem?> UpsertReturningAsync(SsKeyOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SsKeyOnlyItem>> AllAsync(CancellationToken ct = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class EmptyUpsertSetIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public EmptyUpsertSetIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE [SsKeyOnlyItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY);";

    [SkippableFact]
    public async Task KeyOnlyEntityUpsertEmitsValidSqlAndConflictIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "keyonly");
        var store = harness.GetRequiredService<SsKeyOnlyItemStore>();

        await store.UpsertAsync(new SsKeyOnlyItem { Id = null });
        var id = Assert.Single(await store.AllAsync()).Id;
        Assert.NotNull(id);

        await store.UpsertAsync(new SsKeyOnlyItem { Id = id });
        Assert.Single(await store.AllAsync());

        var conflict = await store.UpsertReturningAsync(new SsKeyOnlyItem { Id = id });
        Assert.NotNull(conflict);
        Assert.Equal(id, conflict!.Id);
        Assert.Single(await store.AllAsync());

        var inserted = await store.UpsertReturningAsync(new SsKeyOnlyItem { Id = null });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.Id);
        Assert.Equal(2, (await store.AllAsync()).Count);
    }
}
