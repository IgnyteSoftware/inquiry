using Inquiry.Entities;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("ExistsWidget")]
public sealed class ExistsWidget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("IsDeleted"), InquirySoftDelete]
    public bool IsDeleted { get; set; }
}

public partial class ExistsWidgetStore : InquiryStore<ExistsWidget>
{
    [InquiryInsert]
    public partial Task<ExistsWidget?> InsertAsync(ExistsWidget widget, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    [InquiryExists]
    public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Name")]
    public partial Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
}

[Collection(MariaDbCollection.Name)]
public sealed class ExistsIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public ExistsIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private const string Ddl =
        "CREATE TABLE `ExistsWidget` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY, `Name` VARCHAR(255) NOT NULL, `IsDeleted` TINYINT(1) NOT NULL DEFAULT 0);";

    [SkippableFact]
    public async Task AnyReflectsWhetherTableHasRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
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
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
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
        var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "exists");
        await using var _ = harness;
        var store = harness.GetRequiredService<ExistsWidgetStore>();
        var inserted = await store.InsertAsync(new ExistsWidget { Name = "Alpha" });

        Assert.True(await store.ExistsByNameAsync("Alpha"));
        await store.SoftDeleteAsync(inserted!.Id);
        Assert.False(await store.ExistsByNameAsync("Alpha"));
        Assert.False(await store.AnyAsync());
    }
}
