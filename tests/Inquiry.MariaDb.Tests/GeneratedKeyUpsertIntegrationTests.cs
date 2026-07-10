using Inquiry.MariaDb.Tests.Fixtures;

namespace Inquiry.MariaDb.Tests;

[Collection(MariaDbCollection.Name)]
public sealed class GeneratedKeyUpsertIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;

    private const string Ddl =
        "CREATE TABLE `TGeneratedItem` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY, `Name` VARCHAR(255) NOT NULL);";

    public GeneratedKeyUpsertIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UpsertReturningUsesGeneratedKeyWhenKeyIsNull()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var returned = await store.UpsertReturningAsync(new GeneratedItem { Name = "Generated" });

        Assert.NotNull(returned);
        Assert.NotNull(returned.Id);
        Assert.True(returned.Id > 0);
        Assert.Equal("Generated", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningInsertsExplicitMissingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var returned = await store.UpsertReturningAsync(new GeneratedItem { Id = 42, Name = "Explicit" });

        Assert.NotNull(returned);
        Assert.Equal(42L, returned.Id);
        Assert.Equal("Explicit", returned.Name);
    }

    [SkippableFact]
    public async Task UpsertReturningUpdatesExistingGeneratedKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        await store.UpsertReturningAsync(new GeneratedItem { Id = 7, Name = "Original" });
        var returned = await store.UpsertReturningAsync(new GeneratedItem { Id = 7, Name = "Updated" });

        Assert.NotNull(returned);
        Assert.Equal(7L, returned.Id);
        Assert.Equal("Updated", returned.Name);
    }

    [SkippableFact]
    public async Task ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gkupsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "A" });
        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "B" });

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }
}
