using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.DependencyInjection;
using Inquiry.Entities;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("VersionedDoc")]
public sealed class VersionedDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Title")]
    public string Title { get; set; } = string.Empty;

    [InquiryConcurrencyToken]
    public int Version { get; set; }
}

public partial class VersionedDocStore : InquiryStore<VersionedDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<VersionedDoc?> InsertAsync(VersionedDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<VersionedDoc?> ByIdAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(VersionedDoc doc, CancellationToken cancellationToken = default);

    [InquiryDelete]
    public partial Task<bool> DeleteAsync(VersionedDoc doc, CancellationToken cancellationToken = default);
}

/// <summary>
/// End-to-end optimistic-concurrency behaviour against real SQLite: an ORM-managed version is bumped
/// on a successful update, a stale version makes update/delete a no-op (false by default, throwing
/// when <c>ThrowOnConcurrencyConflict</c> is enabled), and the version composes into the WHERE so the
/// row is matched only when the in-memory version still equals the stored one.
/// </summary>
public sealed class ConcurrencyIntegrationTests
{
    private const string Ddl =
        "CREATE TABLE VersionedDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, Version INTEGER NOT NULL DEFAULT 0);";

    [Fact]
    public async Task SuccessfulUpdateBumpsVersion()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Concurrency");
        var store = harness.GetRequiredService<VersionedDocStore>();
        var doc = await store.InsertAsync(new VersionedDoc { Title = "v0" });

        doc!.Title = "v1";
        Assert.True(await store.UpdateAsync(doc));

        var reloaded = await store.ByIdAsync(doc.Id);
        Assert.Equal(1, reloaded!.Version);
        Assert.Equal("v1", reloaded.Title);
    }

    [Fact]
    public async Task StaleUpdateReturnsFalseAndDoesNotApply()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Concurrency");
        var store = harness.GetRequiredService<VersionedDocStore>();
        var stale = await store.InsertAsync(new VersionedDoc { Title = "v0" });

        // A concurrent writer advances the row to Version 1.
        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "winner";
        Assert.True(await store.UpdateAsync(fresh));

        // The stale copy (still Version 0) must not overwrite the winner.
        stale.Title = "loser";
        Assert.False(await store.UpdateAsync(stale));

        var reloaded = await store.ByIdAsync(stale.Id);
        Assert.Equal("winner", reloaded!.Title);
        Assert.Equal(1, reloaded.Version);
    }

    [Fact]
    public async Task StaleDeleteReturnsFalseAndCurrentDeleteSucceeds()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Concurrency");
        var store = harness.GetRequiredService<VersionedDocStore>();
        var stale = await store.InsertAsync(new VersionedDoc { Title = "v0" });

        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "bumped";
        await store.UpdateAsync(fresh); // row now Version 1

        Assert.False(await store.DeleteAsync(stale)); // stale Version 0 — no match
        Assert.NotNull(await store.ByIdAsync(stale.Id));

        var current = await store.ByIdAsync(stale.Id);
        Assert.True(await store.DeleteAsync(current!)); // current Version 1 — deletes
        Assert.Null(await store.ByIdAsync(stale.Id));
    }

    [Fact]
    public async Task StaleUpdateThrowsWhenOptionEnabled()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Concurrency");

        await using var throwing = new ServiceCollection()
            .AddInquiry(o => o.ThrowOnConcurrencyConflict = true, typeof(VersionedDocStore).Assembly)
            .AddInquirySqlite(harness.ConnectionString)
            .BuildServiceProvider();
        var store = throwing.GetRequiredService<VersionedDocStore>();

        var stale = await store.InsertAsync(new VersionedDoc { Title = "v0" });
        var fresh = await store.ByIdAsync(stale!.Id);
        fresh!.Title = "winner";
        await store.UpdateAsync(fresh); // row now Version 1

        stale.Title = "loser";
        await Assert.ThrowsAsync<InquiryConcurrencyException>(() => store.UpdateAsync(stale));
    }
}
