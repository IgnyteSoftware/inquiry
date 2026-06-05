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
