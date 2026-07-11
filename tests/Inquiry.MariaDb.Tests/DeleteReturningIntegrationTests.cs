using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.DependencyInjection;
using Inquiry.MariaDb.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class DeleteReturningIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE `DeleteReturningItem` (
            `Id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            `Name` LONGTEXT NOT NULL,
            `Version` INT NOT NULL DEFAULT 0
        );
        """;

    private readonly MariaDbContainerFixture _fixture;

    public DeleteReturningIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DeleteReturningReturnsDeletedRowAndNullForMissingRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "delreturn");
        var store = harness.GetRequiredService<DeleteReturningItemStore>();
        var inserted = await store.InsertAsync(new DeleteReturningItem { Name = "present" });
        var item = inserted!;

        var deleted = await store.DeleteReturningAsync(item);
        var missing = await store.DeleteReturningAsync(item);

        Assert.NotNull(deleted);
        Assert.Equal(item.Id, deleted.Id);
        Assert.Equal("present", deleted.Name);
        Assert.Null(missing);
        Assert.Null(await store.ByIdAsync(item.Id));
    }

    [SkippableFact]
    public async Task DeleteReturningHonorsConcurrencyToken()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "delreturnconc");
        var store = harness.GetRequiredService<DeleteReturningItemStore>();
        var stale = await store.InsertAsync(new DeleteReturningItem { Name = "v0" });
        var current = new DeleteReturningItem { Id = stale!.Id, Name = "v1", Version = stale.Version };
        Assert.True(await store.UpdateAsync(current));

        Assert.Null(await store.DeleteReturningAsync(stale));
        var refreshed = await store.ByIdAsync(stale.Id);
        var deleted = await store.DeleteReturningAsync(refreshed!);

        Assert.NotNull(deleted);
        Assert.Equal(1, deleted.Version);
        Assert.Null(await store.ByIdAsync(stale.Id));
    }

    [SkippableFact]
    public async Task StaleDeleteReturningThrowsWhenConcurrencyOptionEnabled()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "delreturnthrow");
        var setupStore = harness.GetRequiredService<DeleteReturningItemStore>();
        var stale = await setupStore.InsertAsync(new DeleteReturningItem { Name = "v0" });
        var current = new DeleteReturningItem { Id = stale!.Id, Name = "v1", Version = stale.Version };
        Assert.True(await setupStore.UpdateAsync(current));

        // The option is scoped to this service provider; disposing it restores the test process to
        // the default configuration without mutating static/global state.
        await using var throwing = new ServiceCollection()
            .AddInquiry(o => o.ThrowOnConcurrencyConflict = true, typeof(DeleteReturningItemStore).Assembly)
            .AddInquiryMariaDb(harness.ConnectionString)
            .BuildServiceProvider();
        var throwingStore = throwing.GetRequiredService<DeleteReturningItemStore>();

        await Assert.ThrowsAsync<InquiryConcurrencyException>(() => throwingStore.DeleteReturningAsync(stale));
        Assert.NotNull(await setupStore.ByIdAsync(stale.Id));
    }
}
