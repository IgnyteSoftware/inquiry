using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;
using Xunit;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Eager-load sibling of bug #49 on SQL Server (the provider that rejected the unsigned DbTypes):
/// a uint KEY above int.MaxValue. The eager-load emitter binds the key through
/// new InquiryParameter(name, rawUintValue, DbType.Int32). Before the runtime binder reinterpreted
/// the value, SqlClient's checked Convert.ToInt32(uint) threw OverflowException.
/// uint 3_000_000_000 → int -1_294_967_296 → fits INT and reads back exactly.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class UnsignedKeyEagerIntegrationTests
{
    private const uint AboveIntMax = 3_000_000_000u; // > int.MaxValue (2_147_483_647)

    private readonly SqlServerContainerFixture _fixture;

    public UnsignedKeyEagerIntegrationTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task EagerLoadByUnsignedKey_AboveIntMax_RoundTripsWithChildren()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedKeyEagerSqlServerDdl, "UnsignedKeyEager");
        var parents = harness.GetRequiredService<UnsignedKeyParentStore>();
        var children = harness.GetRequiredService<UnsignedKeyChildStore>();

        await parents.InsertAsync(new UnsignedKeyParent { Id = AboveIntMax, Name = "Root" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_000_000_000u, ParentId = AboveIntMax, Label = "A" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_100_000_000u, ParentId = AboveIntMax, Label = "B" });

        // Eager-load by the above-int.MaxValue key — the bind path that previously overflowed.
        var loaded = await parents.GetWithChildrenAsync(AboveIntMax);

        Assert.NotNull(loaded);
        Assert.Equal(AboveIntMax, loaded!.Id);
        Assert.Equal("Root", loaded.Name);
        Assert.Equal(2, loaded.Children.Count);
        Assert.Equal(new[] { "A", "B" }, loaded.Children.Select(c => c.Label).OrderBy(s => s));
        // The child FK (also a uint > int.MaxValue) round-trips too.
        Assert.All(loaded.Children, c => Assert.Equal(AboveIntMax, c.ParentId));
    }
}
