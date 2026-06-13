using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("ComputedPerson")]
public sealed class ComputedPerson
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [InquiryColumn("LastName")]
    public string LastName { get; set; } = string.Empty;

    [InquiryColumn("FullName", Computed = "FirstName || ' ' || LastName")]
    public string FullName { get; set; } = string.Empty;
}

public partial class ComputedPersonStore : InquiryStore<ComputedPerson>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<ComputedPerson?> InsertReturningAsync(ComputedPerson person, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(ComputedPerson person, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<ComputedPerson?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-computed column end-to-end against SQLite: the database computes <c>FullName</c> from the
/// stored DDL expression; insert/update never write it, and reads materialize the computed value.
/// </summary>
public sealed class ComputedColumnIntegrationTests
{
    // Mirrors the generated SQLite computed-column form exactly — the type-less expression form
    // `FullName AS (…)` that SqlBuilder.RenderComputedColumn emits — so this test would catch a
    // DDL-shape regression rather than mask it.
    private const string Ddl = "CREATE TABLE ComputedPerson (Id INTEGER PRIMARY KEY AUTOINCREMENT, FirstName TEXT NOT NULL, LastName TEXT NOT NULL, FullName AS (FirstName || ' ' || LastName));";

    [Fact]
    public async Task ComputedValueIsCalculatedByDatabaseOnInsert()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        // FullName is not set by the caller — the database computes it.
        var inserted = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Ada", LastName = "Lovelace" }))!;
        Assert.Equal("Ada Lovelace", inserted.FullName);

        var loaded = (await store.SelectByKeyAsync(inserted.Id))!;
        Assert.Equal("Ada Lovelace", loaded.FullName);
    }

    [Fact]
    public async Task ComputedValueTracksUpdatesToSourceColumns()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Computed");
        var store = harness.GetRequiredService<ComputedPersonStore>();

        var doc = (await store.InsertReturningAsync(new ComputedPerson { FirstName = "Grace", LastName = "Hopper" }))!;

        // Update a source column; the computed value recomputes (a caller-set FullName is ignored).
        Assert.True(await store.UpdateAsync(new ComputedPerson { Id = doc.Id, FirstName = "Grace", LastName = "Murray", FullName = "ignored" }));

        var after = (await store.SelectByKeyAsync(doc.Id))!;
        Assert.Equal("Grace Murray", after.FullName);
    }
}
