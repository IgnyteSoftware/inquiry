using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// A mapped property named <c>__gf_TenantId</c> beside a tenant filter (#288). The probe argument and
/// the ambient tenant value must bind to separate parameters on reads and on enforced writes.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReservedFilterNameIntegrationTests
{
    private const long Tenant = 42L;
    private const long OtherTenant = 7L;

    private readonly SqlServerContainerFixture _fixture;
    public ReservedFilterNameIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private static InquiryFilterContext.Scope Scope(long tenantId)
        => InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId });

    private async Task<SqlServerTestHarness> CreateSeededHarnessAsync()
    {
        var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.ReservedFilterNameDocSqlServerDdl, "rfn");
        var store = harness.GetRequiredService<ReservedFilterNameDocStore>();
        await store.InsertAsync(new ReservedFilterNameDoc { TenantId = Tenant, __gf_TenantId = OtherTenant, Title = "own probe 7" });
        await store.InsertAsync(new ReservedFilterNameDoc { TenantId = OtherTenant, __gf_TenantId = OtherTenant, Title = "other probe 7" });
        await store.InsertAsync(new ReservedFilterNameDoc { TenantId = Tenant, __gf_TenantId = Tenant, Title = "own probe 42" });
        return harness;
    }

    [SkippableFact]
    public async Task ReadsBindTheProbeAndTheTenantSeparately()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedFilterNameDocStore>();

        using (Scope(Tenant))
        {
            Assert.Equal("own probe 7", Assert.Single(await store.ByProbeAsync(OtherTenant)).Title);
            Assert.Equal("own probe 7", Assert.Single(await store.WhereProbeAsync(OtherTenant)).Title);
            Assert.Equal("own probe 42", Assert.Single(await store.ByProbeAsync(Tenant)).Title);
        }
    }

    [SkippableFact]
    public async Task EnforcedUpdateStaysInsideTheTenant()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await CreateSeededHarnessAsync();
        var store = harness.GetRequiredService<ReservedFilterNameDocStore>();

        ReservedFilterNameDoc other;
        using (Scope(OtherTenant))
        {
            other = Assert.Single(await store.AllAsync());
        }

        using (Scope(Tenant))
        {
            Assert.False(await store.UpdateAsync(new ReservedFilterNameDoc
            {
                Id = other.Id,
                TenantId = OtherTenant,
                __gf_TenantId = Tenant,
                Title = "hijacked",
            }));

            var own = Assert.Single(await store.ByProbeAsync(Tenant));
            own.Title = "renamed";
            Assert.True(await store.UpdateAsync(own));
            Assert.Equal("renamed", Assert.Single(await store.ByProbeAsync(Tenant)).Title);
        }

        using (Scope(OtherTenant))
        {
            Assert.Equal("other probe 7", Assert.Single(await store.AllAsync()).Title);
        }
    }
}
