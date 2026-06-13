using System.Threading;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("AuditUserDoc")]
public sealed class AuditUserDoc
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn]
    public string Title { get; set; } = string.Empty;

    [InquiryCreatedBy]
    public string? CreatedBy { get; set; }

    [InquiryModifiedBy]
    public string? ModifiedBy { get; set; }
}

public partial class AuditUserDocStore : InquiryStore<AuditUserDoc>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<AuditUserDoc?> InsertReturningAsync(AuditUserDoc doc, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(AuditUserDoc doc, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<AuditUserDoc?> SelectByKeyAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>[InquiryCreatedBy]</c>/<c>[InquiryModifiedBy]</c> end-to-end: insert stamps both from the
/// ambient <see cref="InquiryAuditContext"/>; update advances ModifiedBy under a new user while the
/// stored CreatedBy survives — even when the updated instance carries a default CreatedBy.
/// </summary>
public sealed class AuditUserIntegrationTests
{
    private const string Ddl = "CREATE TABLE AuditUserDoc (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, CreatedBy TEXT NULL, ModifiedBy TEXT NULL);";

    [Fact]
    public async Task InsertStampsBothFromAmbientUser()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AuditUser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        AuditUserDoc inserted;
        using (InquiryAuditContext.BeginScope("alice"))
        {
            inserted = (await store.InsertReturningAsync(new AuditUserDoc { Title = "spec" }))!;
        }

        Assert.Equal("alice", inserted.CreatedBy);
        Assert.Equal("alice", inserted.ModifiedBy);
    }

    [Fact]
    public async Task UpdateAdvancesModifiedByAndCannotClobberCreatedBy()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AuditUser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        long id;
        using (InquiryAuditContext.BeginScope("alice"))
        {
            id = (await store.InsertReturningAsync(new AuditUserDoc { Title = "v1" }))!.Id;
        }

        // Update under a different user via a *constructed* entity whose CreatedBy is null.
        using (InquiryAuditContext.BeginScope("bob"))
        {
            Assert.True(await store.UpdateAsync(new AuditUserDoc { Id = id, Title = "v2" }));
        }

        var after = (await store.SelectByKeyAsync(id))!;
        Assert.Equal("v2", after.Title);
        Assert.Equal("alice", after.CreatedBy);   // immutable — never clobbered
        Assert.Equal("bob", after.ModifiedBy);    // advanced to the new user
    }

    [Fact]
    public async Task SuppliedCreatedByIsPreserved()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "AuditUser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        using (InquiryAuditContext.BeginScope("alice"))
        {
            var doc = await store.InsertReturningAsync(new AuditUserDoc { Title = "imported", CreatedBy = "legacy-system" });
            Assert.Equal("legacy-system", doc!.CreatedBy);   // supplied value kept, not overwritten by "alice"
            Assert.Equal("alice", doc.ModifiedBy);
        }
    }
}
