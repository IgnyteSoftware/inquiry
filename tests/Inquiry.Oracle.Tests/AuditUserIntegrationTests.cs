using Inquiry;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class AuditUserIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public AuditUserIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InsertStampsBothFromAmbientUser()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditUserOracleDdl, "audituser");

        var store = harness.GetRequiredService<AuditUserDocStore>();

        using var _ = InquiryAuditContext.BeginScope("alice");
        var doc = (await store.InsertReturningAsync(new AuditUserDoc { Title = "Hello" }))!;

        Assert.Equal("alice", doc.CreatedBy);
        Assert.Equal("alice", doc.ModifiedBy);
    }

    [SkippableFact]
    public async Task UpdateAdvancesModifiedByAndCannotClobberCreatedBy()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditUserOracleDdl, "audituser");

        var store = harness.GetRequiredService<AuditUserDocStore>();

        AuditUserDoc doc;
        using (InquiryAuditContext.BeginScope("alice"))
        {
            doc = (await store.InsertReturningAsync(new AuditUserDoc { Title = "Created" }))!;
        }

        using (InquiryAuditContext.BeginScope("bob"))
        {
            await store.UpdateAsync(new AuditUserDoc { Id = doc.Id, Title = "Modified" });
        }

        var reloaded = (await store.SelectByKeyAsync(doc.Id))!;

        Assert.Equal("alice", reloaded.CreatedBy);
        Assert.Equal("bob", reloaded.ModifiedBy);
    }

    [SkippableFact]
    public async Task SuppliedCreatedByIsPreserved()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.AuditUserOracleDdl, "audituser");

        var store = harness.GetRequiredService<AuditUserDocStore>();

        using var _ = InquiryAuditContext.BeginScope("alice");
        var doc = (await store.InsertReturningAsync(new AuditUserDoc { Title = "Legacy", CreatedBy = "legacy-system" }))!;

        Assert.Equal("legacy-system", doc.CreatedBy);
        Assert.Equal("alice", doc.ModifiedBy);
    }
}
