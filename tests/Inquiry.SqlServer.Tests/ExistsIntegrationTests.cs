using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("ExistsWidget")]
public sealed class ExistsWidget
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class ExistsWidgetStore : InquiryStore<ExistsWidget>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ExistsWidget?> InsertAsync(ExistsWidget widget, CancellationToken cancellationToken = default);

    [InquiryDeleteOneByKey]
    public partial Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default);

    [InquiryExists]
    public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Name")]
    public partial Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}

[Collection(SqlServerCollection.Name)]
public sealed class ExistsIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public ExistsIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE [ExistsWidget] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Name] NVARCHAR(MAX) NOT NULL, [IsDeleted] BIT NOT NULL DEFAULT 0);";

    [SkippableFact]
    public async Task AnyReflectsWhetherTableHasRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();

        Assert.False(await store.AnyAsync());
        await store.InsertAsync(new ExistsWidget { Name = "Alpha" });
        Assert.True(await store.AnyAsync());
    }

    [SkippableFact]
    public async Task ExistsByNameTestsForAMatch()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();
        await store.InsertAsync(new ExistsWidget { Name = "Alpha" });

        Assert.True(await store.ExistsByNameAsync("Alpha"));
        Assert.False(await store.ExistsByNameAsync("Beta"));
    }

    [SkippableFact]
    public async Task ExistsExcludesSoftDeletedRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();
        var inserted = await store.InsertAsync(new ExistsWidget { Name = "Alpha" });

        Assert.True(await store.ExistsByNameAsync("Alpha"));
        await store.SoftDeleteAsync(inserted!.Id);
        Assert.False(await store.ExistsByNameAsync("Alpha"));
        Assert.False(await store.AnyAsync());
    }
}
