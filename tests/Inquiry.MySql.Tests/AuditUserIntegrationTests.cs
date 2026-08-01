using System.Threading.Tasks;
using Inquiry;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// <c>[InquiryCreatedBy]</c>/<c>[InquiryModifiedBy]</c> end-to-end against real MySQL: insert stamps
/// both from the ambient <see cref="InquiryAuditContext"/>; update advances ModifiedBy under a new
/// user while the stored CreatedBy survives — even when the updated instance carries a default
/// CreatedBy.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class AuditUserIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public AuditUserIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertStampsBothFromAmbientUser()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditUserMySqlDdl, "audituser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        AuditUserDoc inserted;
        using (InquiryAuditContext.BeginScope("alice"))
        {
            inserted = (await store.InsertReturningAsync(new AuditUserDoc { Title = "spec" }))!;
        }

        Assert.Equal("alice", inserted.CreatedBy);
        Assert.Equal("alice", inserted.ModifiedBy);
    }

    [SkippableFact]
    public async Task UpdateAdvancesModifiedByAndCannotClobberCreatedBy()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditUserMySqlDdl, "audituser");
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

    [SkippableFact]
    public async Task SuppliedCreatedByIsPreserved()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, FeatureSchema.AuditUserMySqlDdl, "audituser");
        var store = harness.GetRequiredService<AuditUserDocStore>();

        using (InquiryAuditContext.BeginScope("alice"))
        {
            var doc = await store.InsertReturningAsync(new AuditUserDoc { Title = "imported", CreatedBy = "legacy-system" });
            Assert.Equal("legacy-system", doc!.CreatedBy);
            Assert.Equal("alice", doc.ModifiedBy);
        }
    }
}
