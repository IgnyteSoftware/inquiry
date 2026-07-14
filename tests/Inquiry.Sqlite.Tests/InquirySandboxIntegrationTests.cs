using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

public sealed class InquirySandboxIntegrationTests
{
    [Fact]
    public async Task GeneratedStoreInsertIsVisibleInsideSandboxAndAbsentAfterward()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "Sandbox");
        var sandbox = new InquirySandbox(harness.Services);

        await sandbox.RunAsync(async (context, token) =>
        {
            var store = context.Services.GetRequiredService<GeneratedItemStore>();
            Assert.Equal(1, await store.InsertAsync(new GeneratedItem { Name = "sandbox" }, token));
            Assert.Equal("sandbox", Assert.Single(await store.SelectAllAsync(token)).Name);
        });

        Assert.Equal(0L, await harness.ExecuteScalarAsync("SELECT COUNT(*) FROM TGeneratedItem"));
    }
}
