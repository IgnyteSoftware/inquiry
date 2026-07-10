using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.DependencyInjection;
using Inquiry.Entities;
using Inquiry.PostgreSql.DependencyInjection;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Seeding;
using Inquiry.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.PostgreSql.Tests;

[InquiryTable("SeededItem")]
public sealed class SeededItem
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

public partial class SeededItemStore : InquiryStore<SeededItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SeededItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SeededItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Conventional seeder shape: constructor-injects the generated store, guards on
/// existing rows so re-running is a no-op.</summary>
public sealed class SeededItemSeeder : IInquiryDataSeeder
{
    private readonly SeededItemStore _store;

    public SeededItemSeeder(SeededItemStore store) => _store = store;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if ((await _store.SelectAllAsync(cancellationToken)).Count > 0)
        {
            return;
        }

        await _store.InsertAsync(new SeededItem { Name = "alpha" }, cancellationToken);
        await _store.InsertAsync(new SeededItem { Name = "beta" }, cancellationToken);
    }
}

/// <summary>
/// Data seeding end-to-end: <c>SeedInquiryAsync()</c> runs the registered seeder inside a scope
/// with real generated stores; the conventional if-empty guard makes a second run a no-op.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class DataSeedingIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public DataSeedingIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SeederPopulatesOnceAndIsIdempotentWithGuard()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        const string ddl = """CREATE TABLE "SeededItem" ("Id" BIGSERIAL PRIMARY KEY, "Name" TEXT NOT NULL)""";
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "seed");

        await using var services = new ServiceCollection()
            .AddInquiry(typeof(SeededItemStore).Assembly)
            .AddInquiryPostgreSql(harness.ConnectionString)
            .AddInquirySeeder<SeededItemSeeder>()
            .BuildServiceProvider();

        await services.SeedInquiryAsync();
        await services.SeedInquiryAsync(); // guard makes the second run a no-op

        using var scope = services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SeededItemStore>().SelectAllAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Name == "alpha");
        Assert.Contains(rows, r => r.Name == "beta");
    }
}
