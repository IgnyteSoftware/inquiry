using System.Threading.Tasks;
using Inquiry;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// <c>[InquiryCreatedBy]</c>/<c>[InquiryModifiedBy]</c> end-to-end: insert stamps both from the
/// ambient <see cref="InquiryAuditContext"/>; update advances ModifiedBy under a new user while the
/// stored CreatedBy survives — even when the updated instance carries a default CreatedBy.
/// </summary>
public sealed class AuditUserIntegrationTests
{
    [Fact]
    public async Task InsertStampsBothFromAmbientUser()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditUserSqliteDdl, "AuditUser");
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
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditUserSqliteDdl, "AuditUser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        long id;
        using (InquiryAuditContext.BeginScope("alice"))
        {
            id = (await store.InsertReturningAsync(new AuditUserDoc { Title = "v1" }))!.Id;
        }

        using (InquiryAuditContext.BeginScope("bob"))
        {
            Assert.True(await store.UpdateAsync(new AuditUserDoc { Id = id, Title = "v2" }));
        }

        var after = (await store.SelectByKeyAsync(id))!;
        Assert.Equal("v2", after.Title);
        Assert.Equal("alice", after.CreatedBy);
        Assert.Equal("bob", after.ModifiedBy);
    }

    [Fact]
    public async Task SuppliedCreatedByIsPreserved()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.AuditUserSqliteDdl, "AuditUser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        using (InquiryAuditContext.BeginScope("alice"))
        {
            var doc = await store.InsertReturningAsync(new AuditUserDoc { Title = "imported", CreatedBy = "legacy-system" });
            Assert.Equal("legacy-system", doc!.CreatedBy);
            Assert.Equal("alice", doc.ModifiedBy);
        }
    }
}
