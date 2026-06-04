using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class GuidKeyUpsertTests
{
    private readonly MySqlContainerFixture _fixture;
    public GuidKeyUpsertTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE TGuidItem (
            Id CHAR(36) NOT NULL DEFAULT (UUID()) PRIMARY KEY,
            Name VARCHAR(100) NOT NULL
        );
        """;

    [SkippableFact]
    public async Task NullKeyLetsDatabaseGenerateTheGuid()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_gen");
        var store = harness.GetRequiredService<GuidItemStore>();

        var saved = await store.UpsertReturningAsync(new GuidItem { Id = null, Name = "A" });
        Assert.NotNull(saved);
        Assert.NotNull(saved!.Id);
        Assert.NotEqual(Guid.Empty, saved.Id!.Value);
    }

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameExplicitKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_conc");
        var store = harness.GetRequiredService<GuidItemStore>();

        var key = Guid.NewGuid();
        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism).Select(i => new GuidItem { Id = key, Name = "Co_" + i }).ToArray();

        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Contains(all[0].Name, inputs.Select(i => i.Name));
    }
}
