using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class GeneratedKeyUpsertIntegrationTests
{
    [Fact]
    public async Task UpsertReturningUsesGeneratedKeyWhenKeyIsNull()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "GeneratedKeyUpsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var returned = await store.UpsertReturningAsync(new GeneratedItem { Name = "Generated" });

        Assert.NotNull(returned);
        Assert.NotNull(returned.Id);
        Assert.True(returned.Id > 0);
        Assert.Equal("Generated", returned.Name);
    }

    [Fact]
    public async Task UpsertReturningInsertsExplicitMissingGeneratedKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "GeneratedKeyUpsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var returned = await store.UpsertReturningAsync(new GeneratedItem { Id = 42, Name = "Explicit" });

        Assert.NotNull(returned);
        Assert.Equal(42, returned.Id);
        Assert.Equal("Explicit", returned.Name);
    }

    [Fact]
    public async Task UpsertReturningUpdatesExistingGeneratedKey()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "GeneratedKeyUpsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        await store.UpsertReturningAsync(new GeneratedItem { Id = 7, Name = "Original" });
        var returned = await store.UpsertReturningAsync(new GeneratedItem { Id = 7, Name = "Updated" });

        Assert.NotNull(returned);
        Assert.Equal(7, returned.Id);
        Assert.Equal("Updated", returned.Name);
    }

    [Fact]
    public async Task ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "GeneratedKeyUpsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "A" });
        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "B" });

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }
}
