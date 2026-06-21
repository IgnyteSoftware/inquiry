using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class GeneratedKeyUpsertTests
{
    private readonly MySqlContainerFixture _fixture;
    public GeneratedKeyUpsertTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE TGeneratedItem (
            Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            Name VARCHAR(100) NOT NULL
        );
        """;

    [SkippableFact]
    public async Task NullKeyLetsDatabaseGenerateTheKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gen_key_gen");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var saved = await store.UpsertReturningAsync(new GeneratedItem { Id = null, Name = "A" });
        Assert.NotNull(saved);
        Assert.NotNull(saved!.Id);
        Assert.True(saved.Id!.Value > 0);
    }

    [SkippableFact]
    public async Task ExplicitNonNullKeyUpsertReturningReadsTheInsertedRow()
    {
        // #53: MySQL does not update LAST_INSERT_ID() for an explicit-value insert, and the ON DUPLICATE
        // UPDATE branch never fires for a NEW row — so a returning SELECT keyed on LAST_INSERT_ID() read a
        // stale/wrong row (or none). The returning SELECT must key on the supplied @Id instead.
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gen_key_explicit");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        // Seed an auto-generated row first; on a reused pooled connection this leaves LAST_INSERT_ID()
        // pointing at that row, which the buggy returning SELECT would read back instead of the new row.
        var auto = await store.UpsertReturningAsync(new GeneratedItem { Id = null, Name = "auto" });
        Assert.NotNull(auto);

        // Insert a NEW row with an explicit, non-contiguous key.
        var inserted = await store.UpsertReturningAsync(new GeneratedItem { Id = 9999, Name = "explicit" });
        Assert.NotNull(inserted);
        Assert.Equal(9999L, inserted!.Id!.Value);
        Assert.Equal("explicit", inserted.Name);

        // Upserting the same explicit key again updates and returns that same row (ON DUPLICATE branch).
        var updated = await store.UpsertReturningAsync(new GeneratedItem { Id = 9999, Name = "explicit-v2" });
        Assert.NotNull(updated);
        Assert.Equal(9999L, updated!.Id!.Value);
        Assert.Equal("explicit-v2", updated.Name);
    }

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameExplicitKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gen_key_conc");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism).Select(i => new GeneratedItem { Id = 5, Name = "Co_" + i }).ToArray();

        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Contains(all[0].Name, inputs.Select(i => i.Name));
    }
}
