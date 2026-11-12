using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

public abstract class TrackedRow<TKey>
{
    [InquiryKey]
    public TKey Id { get; set; } = default!;

    [InquiryColumn("tenant_id")]
    public int TenantId { get; set; }
}

[InquiryTable("TrackedNote")]
public sealed class TrackedNote : TrackedRow<long>
{
    [InquiryColumn]
    public string Body { get; set; } = string.Empty;
}

public partial class TrackedNoteStore : InquiryStore<TrackedNote>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(TrackedNote note, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<TrackedNote?> GetAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(TrackedNote note, CancellationToken cancellationToken = default);
}

/// <summary>
/// An entity whose key and a column are declared on a generic base class: the inherited properties
/// are written, read and used as the key like the entity's own.
/// </summary>
public sealed class InheritedPropertyIntegrationTests
{
    private const string Ddl = "CREATE TABLE TrackedNote (tenant_id INTEGER NOT NULL, Id INTEGER NOT NULL PRIMARY KEY, Body TEXT NOT NULL);";

    [Fact]
    public async Task InheritedKeyAndColumnRoundTrip()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "InheritedProperty");
        var store = harness.GetRequiredService<TrackedNoteStore>();

        await store.InsertAsync(new TrackedNote { Id = 5, TenantId = 42, Body = "first" });
        Assert.True(await store.UpdateAsync(new TrackedNote { Id = 5, TenantId = 43, Body = "second" }));

        var loaded = await store.GetAsync(5);
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.Id);
        Assert.Equal(43, loaded.TenantId);
        Assert.Equal("second", loaded.Body);
    }
}
